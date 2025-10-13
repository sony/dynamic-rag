module "dynamic-rag" {
  source = "./module/dynamic-rag"

  name                       = "dynamic-rag"
  key_vault                  = azurerm_key_vault.vault
  resource_group             = azurerm_resource_group.rg
  azurerm_container_registry = azurerm_container_registry.acr
  api_management             = azurerm_api_management.api-management
  container_app_env          = azurerm_container_app_environment.aca-env

  # database credentials
  administrator_login        = var.administrator_login
  administrator_password     = var.administrator_password

}
