output "app_url" {
  description = "Public URL of the application"
  value = (
    var.domain_name != ""
    ? "https://${var.domain_name}"
    : "https://${aws_cloudfront_distribution.main.domain_name}"
  )
}

output "cloudfront_domain" {
  description = "CloudFront distribution domain (always available, regardless of custom domain)"
  value       = aws_cloudfront_distribution.main.domain_name
}

output "cloudfront_distribution_id" {
  description = "CloudFront distribution ID — needed by the frontend deploy script for cache invalidation"
  value       = aws_cloudfront_distribution.main.id
}

output "s3_bucket_name" {
  description = "S3 bucket that holds the Angular SPA files"
  value       = aws_s3_bucket.frontend.bucket
}

output "ec2_public_ip" {
  description = "Elastic IP of the backend EC2 instance (SSH / debugging)"
  value       = aws_eip.backend.public_ip
}

output "certificate_validation_records" {
  description = <<-EOT
    DNS records required for ACM certificate validation.
    Only populated when domain_name is set but route53_zone_id is not
    (i.e. you are managing DNS outside of Route53).
    Add these CNAME records at your DNS provider before running terraform apply again.
  EOT
  value = (
    var.domain_name != "" && var.route53_zone_id == ""
    ? [for dvo in aws_acm_certificate.main[0].domain_validation_options : {
      name  = dvo.resource_record_name
      type  = dvo.resource_record_type
      value = dvo.resource_record_value
    }]
    : []
  )
}

output "next_steps" {
  description = "Reminder of post-apply deployment steps"
  value       = <<-EOT
    Infrastructure ready. Next:
      1. Deploy the backend:  bash deploy/scripts/deploy-backend.sh
      2. Deploy the frontend: bash deploy/scripts/deploy-frontend.sh
    SSH to the instance:     ssh -i ~/.ssh/<key>.pem ec2-user@${aws_eip.backend.public_ip}
    Tail logs:               sudo journalctl -u planningpoker -f
  EOT
}
