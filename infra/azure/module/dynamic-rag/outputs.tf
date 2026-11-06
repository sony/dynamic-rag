output "server_api_key" {
  description = "API key required by the dynamic-rag server (X-API-Key header) for direct REST/MCP clients."
  value       = random_password.server_api_key.result
  sensitive   = true
}
