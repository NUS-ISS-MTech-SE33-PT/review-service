terraform {
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "6.11.0"
    }
  }
}

provider "aws" {
  region = "ap-southeast-1"
}

variable "image_uri" {
  description = "The full ECR image URI to deploy"
  type        = string
}

data "terraform_remote_state" "infra_vpc" {
  backend = "s3"
  config = {
    bucket = "terraform-state-bucket-d55fab12"
    key    = "prod/infra/vpc/terraform.tfstate"
    region = "ap-southeast-1"
  }
}

data "terraform_remote_state" "infra_ecs" {
  backend = "s3"
  config = {
    bucket = "terraform-state-bucket-d55fab12"
    key    = "prod/infra/ecs/terraform.tfstate"
    region = "ap-southeast-1"
  }
}

resource "aws_ecs_task_definition" "review_service_task" {
  family                   = "review-service-task"
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = "256"
  memory                   = "512"
  execution_role_arn       = data.terraform_remote_state.infra_iam.outputs.ecs_task_execution_role_arn

  container_definitions = jsonencode([
    {
      name      = "review-service-container"
      image     = var.image_uri
      essential = true
      portMappings = [
        {
          containerPort = 80
          protocol      = "tcp"
        }
      ]
    }
  ])
}

resource "aws_ecs_service" "review_service" {
  name            = "review-service"
  cluster         = data.terraform_remote_state.infra_ecs.outputs.aws_ecs_cluster_prod_id
  task_definition = aws_ecs_task_definition.review_service_task.arn
  desired_count   = 1
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = data.terraform_remote_state.infra_vpc.outputs.aws_subnet_ecs_subnet_ids
    assign_public_ip = true
    security_groups  = [data.terraform_remote_state.infra_vpc.outputs.aws_security_group_ecs_sg_id]
  }
}
