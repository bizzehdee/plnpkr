# ACM certificate — only created when a custom domain is provided.
# Must live in us-east-1 because CloudFront only accepts certificates from that region.
resource "aws_acm_certificate" "main" {
  count             = var.domain_name != "" ? 1 : 0
  provider          = aws.us_east_1
  domain_name       = var.domain_name
  validation_method = "DNS"

  lifecycle {
    create_before_destroy = true
  }

  tags = {
    Name = "${var.app_name}"
  }
}

# When a Route53 zone is provided, create the CNAME records automatically so
# certificate validation completes without manual intervention.
resource "aws_route53_record" "cert_validation" {
  for_each = (
    var.domain_name != "" && var.route53_zone_id != ""
    ? {
      for dvo in aws_acm_certificate.main[0].domain_validation_options : dvo.domain_name => {
        name   = dvo.resource_record_name
        record = dvo.resource_record_value
        type   = dvo.resource_record_type
      }
    }
    : {}
  )

  allow_overwrite = true
  name            = each.value.name
  records         = [each.value.record]
  ttl             = 60
  type            = each.value.type
  zone_id         = var.route53_zone_id
}

resource "aws_acm_certificate_validation" "main" {
  count    = var.domain_name != "" && var.route53_zone_id != "" ? 1 : 0
  provider = aws.us_east_1

  certificate_arn         = aws_acm_certificate.main[0].arn
  validation_record_fqdns = [for r in aws_route53_record.cert_validation : r.fqdn]
}

# Point the custom domain at CloudFront via an alias (A) record.
resource "aws_route53_record" "app" {
  count   = var.domain_name != "" && var.route53_zone_id != "" ? 1 : 0
  zone_id = var.route53_zone_id
  name    = var.domain_name
  type    = "A"

  alias {
    name                   = aws_cloudfront_distribution.main.domain_name
    zone_id                = aws_cloudfront_distribution.main.hosted_zone_id
    evaluate_target_health = false
  }
}
