variable "app_name" {
  # Renamed with the project (TeamTools, formerly plnpkr). Changing this on an EXISTING stack
  # replaces every resource named from it — the S3 bucket and the EC2 instance included — so a
  # stack created under the old default should pin app_name = "planning-poker" in terraform.tfvars.
  description = "Name prefix for all AWS resources"
  type        = string
  default     = "teamtools"
}

variable "aws_region" {
  description = "AWS region for EC2, S3, and supporting resources"
  type        = string
  default     = "eu-west-1"
}

variable "domain_name" {
  description = "Custom domain (e.g. teamtools.example.com). Leave empty to use the CloudFront *.cloudfront.net domain."
  type        = string
  default     = ""
}

variable "route53_zone_id" {
  description = "Route53 hosted zone ID. When set alongside domain_name, DNS records are created automatically."
  type        = string
  default     = ""
}

variable "ec2_instance_type" {
  description = "EC2 instance type. t3.micro is free-tier eligible for 12 months; t4g.nano (~$3/mo) is the cheapest thereafter."
  type        = string
  default     = "t3.micro"
}

variable "ec2_key_pair_name" {
  description = "Name of an existing EC2 key pair for SSH access to the backend instance"
  type        = string
}

variable "cloudfront_price_class" {
  description = "CloudFront price class. PriceClass_100 = US/Europe only (cheapest). PriceClass_All = global."
  type        = string
  default     = "PriceClass_100"
}

variable "database_provider" {
  description = "Database provider: Sqlite | SqlServer | PostgreSql"
  type        = string
  default     = "Sqlite"
}

variable "jira_enabled" {
  description = "Enable the Jira integration"
  type        = bool
  default     = false
}

variable "ado_enabled" {
  description = "Enable the Azure DevOps integration"
  type        = bool
  default     = false
}

variable "jira_client_id" {
  description = "Jira OAuth client ID (only needed when jira_enabled = true)"
  type        = string
  default     = ""
  sensitive   = true
}

variable "jira_client_secret" {
  description = "Jira OAuth client secret (only needed when jira_enabled = true)"
  type        = string
  default     = ""
  sensitive   = true
}
