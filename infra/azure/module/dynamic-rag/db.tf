#################################### POSTGRES COMPONENTS ####################################

# 1️⃣ The server itself
resource "azurerm_postgresql_flexible_server" "server" {
  name                          = "${var.name}${var.env}"
  resource_group_name           = var.resource_group.name
  location                      = var.resource_group.location
  version                       = var.postgresql_version
  public_network_access_enabled = var.public
  administrator_login           = var.administrator_login
  administrator_password        = var.administrator_password
  zone                          = var.zone
  storage_mb                    = var.storage_mb
  storage_tier                  = var.storage_tier
  sku_name                      = var.postgres_sku_name
}

# 2️⃣ Your custom database (Azure also creates a “postgres” DB you can ignore)
resource "azurerm_postgresql_flexible_server_database" "db" {
  name      = "${var.name}-db-${var.env}"
  server_id = azurerm_postgresql_flexible_server.server.id
  collation = var.collation
  charset   = var.charset

  lifecycle {
    prevent_destroy = true # prevents DB destroys when TF plan changes unexpectedly
  }
}

# 3️⃣ Allow Vector as an “allowed extension” on the server
resource "azurerm_postgresql_flexible_server_configuration" "allow_vector" {
  name      = "azure.extensions"
  server_id = azurerm_postgresql_flexible_server.server.id
  value     = "vector"
}

# 4️⃣ Firewall rules for your server
resource "azurerm_postgresql_flexible_server_firewall_rule" "spe_vpn_ip_ranges" {
  for_each        = { for idx, rule in var.firewall_rules : idx => rule }
  name            = "firewall-rule-${each.key}"
  server_id       = azurerm_postgresql_flexible_server.server.id
  start_ip_address = each.value.start_ip_address
  end_ip_address   = each.value.end_ip_address
}