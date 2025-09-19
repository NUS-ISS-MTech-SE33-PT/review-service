terraform {
  backend "s3" {
    bucket       = "terraform-state-bucket-d55fab12"
    key          = "prod/services/review/terraform.tfstate"
    region       = "ap-southeast-1"
    use_lockfile = true
    encrypt      = true
  }
}