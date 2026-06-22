# Latest Amazon Linux 2023 x86_64 AMI — owned by Amazon, HVM/gp2 or gp3.
data "aws_ami" "al2023" {
  most_recent = true
  owners      = ["amazon"]

  filter {
    name   = "name"
    values = ["al2023-ami-*-x86_64"]
  }

  filter {
    name   = "virtualization-type"
    values = ["hvm"]
  }
}

resource "aws_security_group" "backend" {
  name        = "${var.app_name}-backend"
  description = "Planning Poker backend — app port + SSH"

  # The app listens on 8080. In production, restrict this to the CloudFront
  # managed prefix list (pl-3b927c52) if you want belt-and-braces security,
  # but leaving it open makes health checks and curl debugging easier.
  ingress {
    description = "ASP.NET Core app"
    from_port   = 8080
    to_port     = 8080
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  ingress {
    description = "SSH"
    from_port   = 22
    to_port     = 22
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name = "${var.app_name}-backend"
  }
}

resource "aws_instance" "backend" {
  ami                    = data.aws_ami.al2023.id
  instance_type          = var.ec2_instance_type
  key_name               = var.ec2_key_pair_name
  vpc_security_group_ids = [aws_security_group.backend.id]

  # Rendered at apply time — Terraform substitutes the variables before base64-encoding.
  user_data = base64encode(templatefile("${path.module}/user_data.sh.tpl", {
    database_provider  = var.database_provider
    jira_enabled       = tostring(var.jira_enabled)
    ado_enabled        = tostring(var.ado_enabled)
    jira_client_id     = var.jira_client_id
    jira_client_secret = var.jira_client_secret
  }))

  root_block_device {
    volume_type           = "gp3"
    volume_size           = 20
    delete_on_termination = true
    encrypted             = true
  }

  tags = {
    Name = "${var.app_name}-backend"
  }

  lifecycle {
    # Prevent accidental replacement (would wipe the SQLite DB on the root volume).
    # Run a snapshot before destroying manually.
    ignore_changes = [ami, user_data]
  }
}

# A static public IP so the CloudFront origin domain never changes across
# instance stop/start cycles.
resource "aws_eip" "backend" {
  domain = "vpc"

  tags = {
    Name = "${var.app_name}-backend"
  }
}

resource "aws_eip_association" "backend" {
  instance_id   = aws_instance.backend.id
  allocation_id = aws_eip.backend.id
}
