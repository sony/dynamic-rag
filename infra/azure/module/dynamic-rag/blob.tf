#################################### AZURE BLOB FOR BACKEND ####################################

resource "azurerm_storage_account" "rag-storage-account" {
  name                      = "${var.name}blob${var.env}"
  location                  = var.resource_group.location
  resource_group_name       = var.resource_group.name
  account_tier              = "Standard"
  account_replication_type  = "LRS"
  account_kind              = "StorageV2"

  blob_properties {
    delete_retention_policy {
      days = 7  # Replace <number_of_days> with the number of days to retain deleted blobs
    }
  }
}

resource "azurerm_storage_container" "rag-storage-container" {
  name                  = "${var.name}-container-${var.env}"
  container_access_type = "private"
  depends_on = [azurerm_storage_account.rag-storage-account]
}