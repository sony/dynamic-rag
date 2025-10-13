variable "name" {
  description = "name for the this deployment of dynamic-rag module"
}

variable "env" {
  description = "environment you're working in (dev, qa or prod)"
  default = "dev"
}


variable "resource_group" {
  description = "Azure resource group"
}

variable "container_app_env" {
  description = "Azure Container App Environment"
}

variable "azurerm_container_registry" {
  description = "Azure Container Registry"
}

variable "server_docker_image" {
  default = {
    "image": "at-pgvector-dotnet-api/server"
    "port": 5272
    "min_instances": 1
    "max_instances": 1
    "cpu": 0.5
    "memory": "1Gi"
  }
}

variable "webapp_docker_image" {
  default = {
    "image": "at-pgvector-dotnet-api/frontend"
    "port": 3000
    "min_instances": 0
    "max_instances": 1
    "cpu": 0.5
    "memory": "1Gi"
  }
}

variable "ip_ranges" {
  default = "IP addresses that can be whitelisted"
}

variable "release_version" {
  default = "latest"
}

# map each release key to the set of versions you care about
variable "release_version_map" {
  description = "Mapping of release keys to frontend/server/blob_trigger tags (etc.)"
  type = map(object({
    webapp_docker_image_version     = string
    server_docker_image_version       = string
  }))
  default = {
    #–– “latest” pseudo‐release ––#
    "latest" = {
      webapp_docker_image_version       = "latest"
      server_docker_image_version       = "latest"
    },
    #–– June 28, 2024 release ––#
    "v1.0.0" = {
      webapp_docker_image_version       = "x.0.zy"
      server_docker_image_version       = "x.0.xz"
    }
    #–– Sept 26, 2025 release ––#
    "v1.1.0" = {
      webapp_docker_image_version       = "x.0.wxy"
      server_docker_image_version       = "x.0.xyz"
    }
    # you can add V1.1.0 = { … } for the next release, etc.
  }
}

variable "api_management" {
  description = "Azure API Management"
}

variable "key_vault" {
  description = "Azure Key Vault"
}

# Database variables
variable "administrator_login" {}

variable "administrator_password" {}

variable "postgresql_version" {
  default = "16"
}
variable "public" {
  default = false
}

variable "zone" {
  default = "1"
}

variable "storage_mb" {
  default = 32768
}

variable "storage_tier" {
  default = "P4"
}

variable "postgres_sku_name" {
  default = "B_Standard_B1ms"
}

variable "firewall_rules" {
  type = any
  default = [
    {
      "name" : "allow-azure-services",
      "start_ip_address" : "0.0.0.0",
      "end_ip_address" : "0.0.0.0"
    }
  ]
}

variable "collation" {
  description = "The collation of the database."
  type        = string
  default     = "en_US.utf8"
}
variable "charset" {
  description = "The character set of the database."
  type        = string
  default     = "UTF8"
}