################################################
# Azure Resource Group
################################################

resource "azurerm_resource_group" "rg" {
  name     = var.resource_group_name
  location = var.azure_region
}

################################################
# Azure Container Registry
################################################

resource "azurerm_container_registry" "acr" {
  name     = "${var.acr_name}${var.env}"
  location = var.azure_region
  resource_group_name     =   azurerm_resource_group.rg.name
  sku                           = var.acr_aku
  admin_enabled                 = true
  public_network_access_enabled = true
  identity {
    type = "SystemAssigned"
  }
}

################################################
# Azure API Management
################################################

resource "azurerm_api_management" "spe-dev-apim" {
   name                = "${var.env}-apim"
   location            = azurerm_resource_group.rg.location
   resource_group_name = azurerm_resource_group.rg.name
   publisher_name      = var.publisher_name
   publisher_email     = var.publisher_email
   sku_name = var.apim_sku_name
}

################################################
# Azure Key Vault
################################################

resource "azurerm_key_vault" "vault" {
  name                = "${var.key_vault_name}-${var.env}"
  location            = var.azure_region
  resource_group_name = azurerm_resource_group.rg.name
  tenant_id           = data.azurerm_client_config.current.tenant_id

  enable_rbac_authorization  = true
  sku_name                   = var.key_vault_sku
  soft_delete_retention_days = 7
  public_network_access_enabled = false
  depends_on = [azurerm_resource_group.rg, azurerm_container_registry.acr]
}

################################################
# Azure Container App Environment
################################################

resource "azurerm_container_app_environment" "aca-env" {
  name                       = "aca-apps-env-${var.env}"
  location                   = azurerm_resource_group.rg.location
  resource_group_name        = azurerm_resource_group.rg.name
  zone_redundancy_enabled = true
  infrastructure_subnet_id = data.azurerm_subnet.subnet.id
  #internal_load_balancer_enabled = true
  workload_profile {
    name = "Consumption"
    workload_profile_type = "Consumption"
    minimum_count = 0
    maximum_count = 50
  }
  lifecycle {
     ignore_changes = [workload_profile, infrastructure_resource_group_name]
  }

  depends_on = [ data.azurerm_subnet.subnet ]

  timeouts {read = "60m"}

}
