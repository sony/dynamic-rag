#################################### AZURE CONTAINER APPLICATION FOR BACKEND ####################################

resource "azurerm_container_app" "backend" {
  name                         = "${var.name}-server-${var.env}"
  container_app_environment_id = var.container_app_env.id
  resource_group_name          = var.resource_group.name
  revision_mode                = "Single"
  workload_profile_name        = "Consumption"
  identity {
    type = "SystemAssigned"
  }
  secret {
    name  = "registry-credentials"
    value = var.azurerm_container_registry.admin_password
  }

  registry {
    server               = var.azurerm_container_registry.login_server
    username             = var.azurerm_container_registry.admin_username
    password_secret_name = "registry-credentials"
  }

  ingress {
    external_enabled = true
    target_port      = var.server_docker_image.port

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  template {
    min_replicas = var.server_docker_image.min_instances
    max_replicas = var.server_docker_image.max_instances
    container {
      name   = "${var.name}-server-${var.env}"
      image  = "${var.azurerm_container_registry.name}.azurecr.io/${var.server_docker_image.image}:${var.release_version_map[var.release_version].server_docker_image_version}"
      cpu    = var.server_docker_image.cpu
      memory = var.server_docker_image.memory

      env{
        name = "PG_USER"
        value = var.administrator_login
      }
      env{
        name = "PG_HOST"
        value = "${azurerm_postgresql_flexible_server.server.name}.postgres.database.azure.com"
      }
      env{
        name = "PG_DATABASE"
        value = azurerm_postgresql_flexible_server_database.db.name
      }
      env{
        name = "PG_PASSWORD"
        value = var.administrator_password
      }
      env{
        name = "PG_PORT"
        value = 5432
      }
      env{
        name = "PORT"
        value = 5272
      }
      env{
        name = "BLOB_STORAGE_CONNECTION_STRING"
        value = azurerm_storage_account.rag-storage-account.primary_connection_string
      }
      env{
        name = "BLOB_STORAGE_RAG_CONTAINER_NAME"
        value = azurerm_storage_container.rag-storage-container.name
      }
      env{
        name = "AZURE_SUBSCRIPTION_ID"
        value = data.azurerm_client_config.current.subscription_id
      }
      env{
        name = "MAX_RESURSIVE_SPLIT_DEPTH"
        value = 500
      }
      env{
        name = "CHUNKING_PARALLELISM"
        value = 5
      }
      env{
        name = "CLIP_MODEL_DEPLOYMENT_NAME"
        value = "openai-clip-image-text-embedd-3"
      }
      env {
        name  = "SSL_VERIFY"
        value = "False"
      }
      env {
        name = "MODEL_SOURCE"
        value = "subscription"
      }
      env {
        name = "PROVIDER"
        value = "azure"
      }
    }
  }
  lifecycle {
    ignore_changes = [registry, secret]
  }

  depends_on = [azurerm_postgresql_flexible_server_database.db,
                azurerm_storage_account.rag-storage-account, azurerm_storage_container.rag-storage-container]
}

resource "azurerm_role_assignment" "backend_aca_to_acr1" {
  scope                = var.azurerm_container_registry.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_container_app.backend.identity[0].principal_id #[each.key].identity[0].principal_id
  depends_on           = [azurerm_container_app.backend]
}

resource "azurerm_role_assignment" "backend_key_vault_secrets_user" {
  scope                = var.key_vault.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_container_app.backend.identity[0].principal_id
}

resource "azurerm_role_assignment" "backend_cognitive_services_user" {
  scope                = "/subscriptions/${data.azurerm_client_config.current.subscription_id}"
  role_definition_name = "Cognitive Services User"
  principal_id         = azurerm_container_app.backend.identity[0].principal_id
}

resource "azurerm_role_assignment" "backend_subscription_reader" {
  scope                = "/subscriptions/${data.azurerm_client_config.current.subscription_id}"
  role_definition_name = "Reader"
  principal_id         = azurerm_container_app.backend.identity[0].principal_id
}

#################################### AZURE CONTAINER APPLICATION FOR FRONTEND ####################################

resource "azurerm_container_app" "frontend" {
  name                         = "${var.name}-client-${var.env}"
  container_app_environment_id = var.container_app_env.id
  resource_group_name          = var.resource_group.name
  revision_mode                = "Single"
  workload_profile_name        = "Consumption"
  identity {
    type = "SystemAssigned"
  }
  secret {
    name  = "registry-credentials"
    value = var.azurerm_container_registry.admin_password
  }

  registry {
    server               = var.azurerm_container_registry.login_server
    username             = var.azurerm_container_registry.admin_username
    password_secret_name = "registry-credentials"
  }

  ingress {
    external_enabled = true
    target_port      = var.webapp_docker_image.port
    dynamic "ip_security_restriction" {
      for_each = toset(var.ip_ranges)
      content {
        name             = "${ip_security_restriction.key}-ip_rule"
        action           = "Allow"
        ip_address_range = ip_security_restriction.key #data.azurerm_api_management.apim.public_ip_addresses[0]  #
      }
    }

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  template {
    min_replicas = var.webapp_docker_image.min_instances
    max_replicas = var.webapp_docker_image.max_instances
    container {
      name   = "${var.name}-client-${var.env}"
      # image  = "${var.container_registry_name}.azurecr.io/${var.webapp_docker_image.image}:${var.webapp_docker_image_version}"
      image  = "${var.azurerm_container_registry.name}.azurecr.io/${var.webapp_docker_image.image}:${var.release_version_map[var.release_version].webapp_docker_image_version}"
      cpu    = var.webapp_docker_image.cpu
      memory = var.webapp_docker_image.memory

      env {
        name  = "VITE_API_URL"
        value = "https://${var.api_management.name}.azure-api.net/${var.name}-server-${var.env}"
      }
      env{
        name = "VITE_OCP_APIM_SUBSCRIPTION_KEY"
        value = azurerm_api_management_subscription.aca-api-subscription.primary_key
      }
    }
  }
  lifecycle {
    ignore_changes = [registry, secret]
  }
}

resource "azurerm_role_assignment" "frontend_aca_to_acr1" {
  scope                = var.azurerm_container_registry.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_container_app.frontend.identity[0].principal_id #[each.key].identity[0].principal_id
  depends_on           = [azurerm_container_app.frontend]
}

resource "azurerm_role_assignment" "frontend_key_vault_secrets_user" {
  scope                = var.key_vault.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_container_app.frontend.identity[0].principal_id
}

#################################### AZURE API MANAGEMENT CONNECTIVITY ####################################

resource "azurerm_api_management_backend" "aca-apim-backend" {
  name                = azurerm_container_app.backend.name
  resource_group_name = var.resource_group.name
  api_management_name = var.api_management.name
  protocol            = "http"
  url                 = "https://${azurerm_container_app.backend.name}.${var.container_app_env.default_domain}"

  credentials {
    header = {
        api-key = "a"
    }
  }
  tls {
    validate_certificate_chain = false
    validate_certificate_name  = false
  }
}

resource "azurerm_api_management_api" "rag-backend-api" {

  name                = azurerm_container_app.backend.name
  resource_group_name = var.resource_group.name
  api_management_name = var.api_management.name
  revision            = "current"
  display_name        = azurerm_container_app.backend.name
  path                = azurerm_container_app.backend.name
  protocols           = ["https"]
  subscription_required = true

  import {
    content_format = "openapi"
    content_value  = templatefile("./module/dynamic-rag/openapi_template.json", {app_name = azurerm_container_app.backend.name})
  }
  #depends_on = [ azurerm_api_management_backend.example ]
}

resource "azurerm_api_management_api_policy" "apiOpenaiPolicy" {
  api_name = azurerm_api_management_api.rag-backend-api.name
  api_management_name = var.api_management.name
  resource_group_name = var.resource_group.name
  xml_content = templatefile("./modules/advtech-azure-apim/generic_policy.xml", {
    backend_name = azurerm_container_app.backend.name
  })
}

resource "azurerm_api_management_subscription" "aca-api-subscription" {
  api_management_name = var.api_management.name
  resource_group_name = var.resource_group.name
  api_id              = azurerm_api_management_api.rag-backend-api.id
  display_name        = azurerm_container_app.backend.name
  state               = "active"
  allow_tracing       = false
  depends_on          = [ azurerm_api_management_api.rag-backend-api ]
}

resource "azurerm_api_management_product" "apim-product" {
  product_id            = "${var.name}-apiproduct-${var.env}" # each.value["product_id"] #var.product_name
  api_management_name   = var.api_management.name
  resource_group_name   = var.resource_group.name
  display_name          = "${var.name}-apiproduct-${var.env}"
  subscription_required = true
  subscriptions_limit   = 1
  approval_required     = true
  published             = true
}

resource "azurerm_api_management_product_api" "apim-product-api" {
  api_name            = azurerm_api_management_api.rag-backend-api.name
  product_id          = azurerm_api_management_product.apim-product.product_id
  api_management_name = var.api_management.name
  resource_group_name = var.resource_group.name
  depends_on = [azurerm_api_management_api.rag-backend-api]
}
