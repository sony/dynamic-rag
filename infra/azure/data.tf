#################################### GLOBALS ####################################
data "azurerm_client_config" "current" {}

data "azurerm_subnet" "subnet" {
  name = "my-subnet"
  virtual_network_name = "my-vnet-name"
  resource_group_name = "my-resource-group"
}