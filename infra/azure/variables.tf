# Resource Group
variable "resource_group_name" {
  description = "name of the resource group"
}

# Azure Region
variable "azure_region" {
  description = "name of the resource group"
}

# To maintain sanity between subscriptions/deployment phases
variable "env" {
  description = "environment you're working in (dev, qa or prod)"
  default = "dev"
}

# Container Registry vars
variable "acr_name" {
  description = "Name of Azure Container Registry where client and server images will be published"
}

variable "acr_aku" {
  description = "SKU for Azure Container Registry"
  default = "Standard"
}

# Axure API Management vars
variable "apim_sku_name" {
  description = "API Management SKU"
  default = "Developer_1"
}

variable "publisher_name" {
  description = "name of publisher"
  default = "Sony"
}

variable "publisher_email" {
  description = "publisher email"
  default = "myemail@mydomain.com"
}

# Azure Key Vault
variable "key_vault_name" {
  description = "Key Vault Name"
}

variable "key_vault_sku" {
  description = "SKU for Azure Key vault"
  default = "standard"
}

# database credentials
variable "administrator_login" {
  description = "login for PGVector DB"
}

variable "administrator_password" {
  description = "password for PGVector DB"
}



