# Deploying dynamic-rag using Terraform

This directory is a how-to guide on deploying ```dynamic-rag``` via Terraform-based IaC. The [azure/](azure) directory is specifically for deployment on Azure.

## Pre-Requisites:
1. An Azure Subscription
2. Virtual Network and Subnet(s) running on the subscription

## Files:

1. [module/dynamic-rag](azure/module/dynamic-rag): Plug-and-play module ready for usage
   * [aca.tf](azure/module/dynamic-rag/aca.tf): Azure Container App deployments, their role assignments and necessary API management routing for backend to be exposed via REST
     * Note: REST endpoint exposed via API management uses this [OpenAPI spec](azure/module/dynamic-rag/openapi_template.json)
   * [blob.tf](azure/module/dynamic-rag/blob.tf): Azure storage account and container needed for storage of assets
   * [data.tf](azure/module/dynamic-rag/data.tf): Runtime configuration for TF Workspace
   * [db.tf](azure/module/dynamic-rag/db.tf): Postgres server, database and on-the-fly vector extension installation + firewall rules for ingress
   * [providers.tf](azure/module/dynamic-rag/providers.tf): Terraform providers
   * [variables.tf](azure/module/dynamic-rag/variables.tf): Terraform variables

2. [data.tf](azure/data.tf): Runtime configuration for TF Workspace
3. [dynamic-rag.tf](azure/dynamic-rag.tf): Actual usage of the [module/dynamic-rag](azure/module/dynamic-rag) module
4. [main.tf](azure/main.tf): Creation of the basics
   * Azure Resource Group
   * Azure Container Registry
   * Azure Key Vault
   * Azure Container App Environment
   * Azure API Management Services
5. [variables.tf](azure/variables.tf): Terraform variables
