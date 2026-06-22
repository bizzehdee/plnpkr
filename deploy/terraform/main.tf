terraform {
  required_version = ">= 1.6"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.0"
    }
  }
}

# Primary region — EC2, S3, and most supporting resources live here.
provider "aws" {
  region = var.aws_region
}

# ACM certificates used by CloudFront must be in us-east-1, regardless of the
# primary region. This second provider alias satisfies that requirement.
provider "aws" {
  alias  = "us_east_1"
  region = "us-east-1"
}

resource "random_id" "suffix" {
  byte_length = 4
}
