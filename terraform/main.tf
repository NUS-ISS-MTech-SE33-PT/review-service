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

data "terraform_remote_state" "infra_iam" {
  backend = "s3"
  config = {
    bucket = "terraform-state-bucket-d55fab12"
    key    = "prod/infra/iam/terraform.tfstate"
    region = "ap-southeast-1"
  }
}

data "terraform_remote_state" "infra_api_gateway" {
  backend = "s3"
  config = {
    bucket = "terraform-state-bucket-d55fab12"
    key    = "prod/infra/api-gateway/terraform.tfstate"
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

  load_balancer {
    target_group_arn = aws_lb_target_group.review_service_target_group.arn
    container_name   = "review-service-container"
    container_port   = 80
  }
}

resource "aws_lb" "review_service_network_load_balancer" {
  name               = "review-service-nlb"
  internal           = true
  load_balancer_type = "network"
  subnets            = data.terraform_remote_state.infra_vpc.outputs.aws_subnet_ecs_subnet_ids
}

resource "aws_lb_target_group" "review_service_target_group" {
  name        = "review-service-target-group"
  port        = 80
  protocol    = "TCP"
  vpc_id      = data.terraform_remote_state.infra_vpc.outputs.aws_vpc_ecs_vpc_id
  target_type = "ip"
}

resource "aws_lb_listener" "review_service_network_load_balancer_listener" {
  load_balancer_arn = aws_lb.review_service_network_load_balancer.arn
  port              = 80
  protocol          = "TCP"

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.review_service_target_group.arn
  }
}

resource "aws_apigatewayv2_integration" "review_service_integration" {
  api_id                 = data.terraform_remote_state.infra_api_gateway.outputs.aws_apigatewayv2_api_makan_go_http_api_id
  integration_type       = "HTTP_PROXY"
  integration_uri        = aws_lb_listener.review_service_network_load_balancer_listener.arn
  connection_type        = "VPC_LINK"
  connection_id          = data.terraform_remote_state.infra_api_gateway.outputs.aws_apigatewayv2_vpc_link_ecs_vpc_link_id
  payload_format_version = "1.0"
  integration_method     = "ANY"
}

resource "aws_apigatewayv2_route" "route" {
  api_id    = data.terraform_remote_state.infra_api_gateway.outputs.aws_apigatewayv2_api_makan_go_http_api_id
  route_key = "ANY /{proxy+}"
  target    = "integrations/${aws_apigatewayv2_integration.review_service_integration.id}"
}