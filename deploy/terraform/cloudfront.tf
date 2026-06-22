locals {
  s3_origin_id  = "S3-Frontend"
  ec2_origin_id = "EC2-Backend"

  # S3 website endpoint — returned without a protocol prefix, ready for use as
  # a CloudFront domain_name. CloudFront connects to this over plain HTTP (the
  # S3 website endpoint doesn't support HTTPS); TLS is terminated at the edge.
  s3_website_endpoint = aws_s3_bucket_website_configuration.frontend.website_endpoint

  # CloudFront uses the Elastic IP directly as the origin domain.  AWS allows
  # raw IPv4 addresses for custom HTTP origins.
  ec2_origin_ip = aws_eip.backend.public_ip
}

resource "aws_cloudfront_distribution" "main" {
  enabled             = true
  is_ipv6_enabled     = true
  default_root_object = "index.html"
  price_class         = var.cloudfront_price_class

  # Only set aliases when a custom domain is provided; omitting them lets
  # CloudFront use its own *.cloudfront.net hostname.
  aliases = var.domain_name != "" ? [var.domain_name] : []

  # ── Origins ────────────────────────────────────────────────────────────────

  origin {
    domain_name = local.s3_website_endpoint
    origin_id   = local.s3_origin_id

    custom_origin_config {
      http_port              = 80
      https_port             = 443
      origin_protocol_policy = "http-only" # S3 website endpoint is HTTP-only
      origin_ssl_protocols   = ["TLSv1.2"]
    }
  }

  origin {
    domain_name = local.ec2_origin_ip
    origin_id   = local.ec2_origin_id

    custom_origin_config {
      http_port              = 8080
      https_port             = 443
      origin_protocol_policy = "http-only" # TLS is terminated at CloudFront
      origin_ssl_protocols   = ["TLSv1.2"]

      # Keep connections alive between CloudFront and EC2 for low-latency
      # WebSocket establishment.
      origin_keepalive_timeout = 60
      origin_read_timeout      = 60
    }
  }

  # ── Cache behaviours (evaluated in order; first match wins) ───────────────

  # SignalR hub — WebSocket + long-poll negotiate.
  # Forward ALL headers so CloudFront passes the HTTP Upgrade handshake
  # through to ASP.NET Core. TTL=0 means nothing is ever cached.
  ordered_cache_behavior {
    path_pattern     = "/hubs/*"
    allowed_methods  = ["DELETE", "GET", "HEAD", "OPTIONS", "PATCH", "POST", "PUT"]
    cached_methods   = ["GET", "HEAD"]
    target_origin_id = local.ec2_origin_id
    compress         = false

    forwarded_values {
      query_string = true
      headers      = ["*"]
      cookies {
        forward = "all"
      }
    }

    viewer_protocol_policy = "redirect-to-https"
    min_ttl                = 0
    default_ttl            = 0
    max_ttl                = 0
  }

  # REST API — no caching, forward the important request headers.
  ordered_cache_behavior {
    path_pattern     = "/api/*"
    allowed_methods  = ["DELETE", "GET", "HEAD", "OPTIONS", "PATCH", "POST", "PUT"]
    cached_methods   = ["GET", "HEAD"]
    target_origin_id = local.ec2_origin_id
    compress         = true

    forwarded_values {
      query_string = true
      headers      = ["Authorization", "Content-Type", "Origin", "Accept"]
      cookies {
        forward = "none"
      }
    }

    viewer_protocol_policy = "redirect-to-https"
    min_ttl                = 0
    default_ttl            = 0
    max_ttl                = 0
  }

  # Health endpoints — used by ALB / uptime monitors, never cached.
  ordered_cache_behavior {
    path_pattern     = "/health*"
    allowed_methods  = ["GET", "HEAD"]
    cached_methods   = ["GET", "HEAD"]
    target_origin_id = local.ec2_origin_id
    compress         = false

    forwarded_values {
      query_string = false
      cookies {
        forward = "none"
      }
    }

    viewer_protocol_policy = "redirect-to-https"
    min_ttl                = 0
    default_ttl            = 0
    max_ttl                = 0
  }

  # Default — serve the Angular SPA from S3 with aggressive caching.
  # Hashed asset filenames (main.abc123.js) are immutable; index.html and
  # config.js are uploaded with cache-control: no-cache by the deploy script.
  default_cache_behavior {
    allowed_methods  = ["GET", "HEAD", "OPTIONS"]
    cached_methods   = ["GET", "HEAD"]
    target_origin_id = local.s3_origin_id
    compress         = true

    forwarded_values {
      query_string = false
      cookies {
        forward = "none"
      }
    }

    viewer_protocol_policy = "redirect-to-https"
    min_ttl                = 0
    default_ttl            = 86400     # 1 day
    max_ttl                = 31536000  # 1 year (for hashed assets)
  }

  # ── SPA deep-link fallback ─────────────────────────────────────────────────
  # S3 returns 403 (key not found with website hosting) for any path that
  # isn't a real file. Map those back to index.html so Angular's router works.
  custom_error_response {
    error_code            = 403
    response_code         = 200
    response_page_path    = "/index.html"
    error_caching_min_ttl = 0
  }

  custom_error_response {
    error_code            = 404
    response_code         = 200
    response_page_path    = "/index.html"
    error_caching_min_ttl = 0
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    # Use the ACM cert when a custom domain is configured; fall back to the
    # default *.cloudfront.net certificate otherwise.
    cloudfront_default_certificate = var.domain_name == ""
    acm_certificate_arn            = var.domain_name != "" ? aws_acm_certificate.main[0].arn : null
    ssl_support_method             = var.domain_name != "" ? "sni-only" : null
    minimum_protocol_version       = var.domain_name != "" ? "TLSv1.2_2021" : null
  }

  tags = {
    Name = "${var.app_name}"
  }

  depends_on = [
    aws_eip_association.backend,
    aws_s3_bucket_website_configuration.frontend,
  ]
}
