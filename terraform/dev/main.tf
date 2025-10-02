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

resource "aws_dynamodb_table" "reviews" {
  name         = "reviews-dev"
  billing_mode = "PAY_PER_REQUEST"

  hash_key  = "spotId"
  range_key = "id"

  attribute {
    name = "spotId"
    type = "S"
  }

  attribute {
    name = "id"
    type = "S"
  }

  global_secondary_index {
    name            = "reviews_by_user"
    hash_key        = "userId"
    range_key       = "createdAt"
    projection_type = "ALL"
  }

  global_secondary_index {
    name            = "reviews_by_createdAt"
    hash_key        = "spotId"
    range_key       = "createdAt"
    projection_type = "ALL"
  }

  attribute {
    name = "userId"
    type = "S"
  }

  attribute {
    name = "createdAt"
    type = "N"
  }

  server_side_encryption {
    enabled = true
  }
}