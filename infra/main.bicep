// LabSeat on Azure: API on App Service (Linux, .NET 8), Azure SQL with Entra-only auth,
// Angular front end on Static Web Apps, telemetry in Application Insights.
// The API reaches SQL with its managed identity, so no database password exists anywhere.
targetScope = 'resourceGroup'

@description('Short prefix for resource names.')
@minLength(3)
@maxLength(12)
param namePrefix string = 'labseat'

@description('Region for everything except Static Web Apps.')
param location string = resourceGroup().location

@description('Static Web Apps is offered in a limited set of regions.')
@allowed(['westeurope', 'eastus2', 'centralus', 'westus2', 'eastasia'])
param webLocation string = 'westeurope'

@description('Object ID of the Microsoft Entra group that administers the SQL server.')
param sqlAdminGroupObjectId string

@description('Display name of that group.')
param sqlAdminGroupName string

@description('Token issuer the API trusts, e.g. https://login.microsoftonline.com/<tenant-id>/v2.0')
param authAuthority string

@description('Audience (application ID URI or client ID) the API expects in tokens.')
param authAudience string

var suffix = uniqueString(resourceGroup().id)
var tags = {
  app: 'labseat'
}

resource logs 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${namePrefix}-logs-${suffix}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-ai-${suffix}'
  location: location
  kind: 'web'
  tags: tags
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: '${namePrefix}-sql-${suffix}'
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: sqlAdminGroupName
      sid: sqlAdminGroupObjectId
      tenantId: subscription().tenantId
      principalType: 'Group'
    }
  }
}

resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'labseat'
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    autoPauseDelay: 60
    minCapacity: json('0.5')
    zoneRedundant: false
  }
}

resource web 'Microsoft.Web/staticSites@2023-12-01' = {
  name: '${namePrefix}-web-${suffix}'
  location: webLocation
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${namePrefix}-plan-${suffix}'
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: 'P0v3'
  }
  properties: {
    reserved: true
  }
}

resource api 'Microsoft.Web/sites@2023-12-01' = {
  name: '${namePrefix}-api-${suffix}'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      alwaysOn: true
      healthCheckPath: '/healthz'
      appSettings: [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: insights.properties.ConnectionString
        }
        {
          name: 'Auth__Authority'
          value: authAuthority
        }
        {
          name: 'Auth__Audience'
          value: authAudience
        }
        {
          name: 'Cors__Origins__0'
          value: 'https://${web.properties.defaultHostname}'
        }
      ]
      connectionStrings: [
        {
          name: 'LabSeat'
          type: 'SQLAzure'
          connectionString: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${database.name};Authentication=Active Directory Managed Identity;Encrypt=True'
        }
      ]
    }
  }
}

output apiUrl string = 'https://${api.properties.defaultHostName}'
output webUrl string = 'https://${web.properties.defaultHostname}'
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
@description('Grant this identity access in the database: CREATE USER [<api name>] FROM EXTERNAL PROVIDER; then EXECUTE on dbo.')
output apiPrincipalId string = api.identity.principalId
