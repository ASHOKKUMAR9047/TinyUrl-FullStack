terraform {
  required_version = ">= 1.3.0"
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
  }
}

provider "azurerm" {
  features {}
}

# 1. Variables
variable "project_name" {
  type        = string
  default     = "tinyurl"
  description = "Unique project prefix for Azure resources"
}

variable "location" {
  type        = string
  default     = "East US"
  description = "Target Azure Region"
}

variable "sql_admin_username" {
  type        = string
  default     = "tinyurladmin"
  description = "Administrator login for SQL Database"
}

variable "sql_admin_password" {
  type        = string
  sensitive   = true
  default     = "P@ssw0rd12345!" # Use a secure secret in real production environments
  description = "Administrator password for SQL Database"
}

# 2. Resource Group
resource "azurerm_resource_group" "rg" {
  name     = "rg-${var.project_name}-prod"
  location = var.location
}

# 3. Storage Account (Used by Azure Functions and Logging)
resource "azurerm_storage_account" "storage" {
  name                     = "st${var.project_name}prod"
  resource_group_name      = azurerm_resource_group.rg.name
  location                 = azurerm_resource_group.rg.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
}

# 4. Azure SQL Server & Database
resource "azurerm_mssql_server" "sql_server" {
  name                         = "sql-${var.project_name}-srv"
  resource_group_name          = azurerm_resource_group.rg.name
  location                     = azurerm_resource_group.rg.location
  version                      = "12.0"
  administrator_login          = var.sql_admin_username
  administrator_login_password = var.sql_admin_password
}

resource "azurerm_mssql_database" "sql_db" {
  name      = "db-${var.project_name}"
  server_id = azurerm_mssql_server.sql_server.id
  sku_name  = "Basic" # Economical tier for development/testing
}

# Allow Azure Services access to the SQL Server
resource "azurerm_mssql_firewall_rule" "allow_azure_services" {
  name             = "AllowAzureServices"
  server_id        = azurerm_mssql_server.sql_server.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

# 5. Service Plan for Web App & Function App
resource "azurerm_service_plan" "app_service_plan" {
  name                = "plan-${var.project_name}-prod"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  os_type             = "Windows"
  sku_name            = "F1" # Free tier for demonstration/sandbox
}

# 6. Web App for hosting the ASP.NET Core API
resource "azurerm_windows_web_app" "web_api" {
  name                = "app-${var.project_name}-api"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  service_plan_id     = azurerm_service_plan.app_service_plan.id

  site_config {
    always_on = false
    application_stack {
      current_stack  = "dotnet"
      dotnet_version = "v9.0"
    }
  }

  connection_string {
    name  = "DefaultConnection"
    type  = "SQLServer"
    value = "Server=tcp:${azurerm_mssql_server.sql_server.fully_qualified_domain_name},1433;Initial Catalog=${azurerm_mssql_database.sql_db.name};Persist Security Info=False;User ID=${var.sql_admin_username};Password=${var.sql_admin_password};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
  }

  app_settings = {
    "WEBSITE_RUN_FROM_PACKAGE" = "1"
  }
}

# 7. Azure Function App for Serverless Cleanup
resource "azurerm_windows_function_app" "cleanup_function" {
  name                = "func-${var.project_name}-cleanup"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  service_plan_id     = azurerm_service_plan.app_service_plan.id

  storage_account_name       = azurerm_storage_account.storage.name
  storage_account_access_key = azurerm_storage_account.storage.primary_access_key

  site_config {
    application_stack {
      dotnet_version = "v9.0"
      use_custom_runtime = true
    }
  }

  app_settings = {
    "FUNCTIONS_WORKER_RUNTIME" = "dotnet-isolated"
    "SqlConnectionString"      = "Server=tcp:${azurerm_mssql_server.sql_server.fully_qualified_domain_name},1433;Initial Catalog=${azurerm_mssql_database.sql_db.name};Persist Security Info=False;User ID=${var.sql_admin_username};Password=${var.sql_admin_password};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
    "WEBSITE_RUN_FROM_PACKAGE" = "1"
  }
}

# Outputs
output "api_web_url" {
  value       = azurerm_windows_web_app.web_api.default_hostname
  description = "The URL of the Web API deployment"
}

output "cleanup_function_url" {
  value       = azurerm_windows_function_app.cleanup_function.default_hostname
  description = "The URL of the Cleanup Function App"
}
