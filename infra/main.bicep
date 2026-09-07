targetScope = 'subscription'

@description('Location for the resource group and the storage account')
param location string = 'eastus2'

@description('App Service Plan SKU. Left at F1 (Free) unless somebody deliberately opts into a billed tier; see resources.bicep for what each one buys.')
param appServicePlanSku string = 'F1'

// Naming: Po{Solution}. Everything app-specific lives in the PoMarriedFight resource group; Key Vault,
// App Insights and Log Analytics stay in PoShared and are referenced as existing, the same layout every
// other Po* app uses.
var resourceGroupName = 'PoMarriedFight'
var sharedResourceGroupName = 'PoShared'
var storageAccountName = 'stpomarriedfight'
var webAppName = 'app-pomarriedfight'
var appServicePlanName = 'asp-pomarriedfight-f1'
var keyVaultName = 'kv-poshared'
var appInsightsName = 'poappideinsights8f9c9a4e'

resource rg 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: resourceGroupName
  location: location
}

module resources 'resources.bicep' = {
  name: 'resources'
  scope: rg
  params: {
    // West US 2: this subscription has no Free-tier quota in East US 2 (SubscriptionIsOverQuotaForSku),
    // and every other Po* F1 plan is West US 2 for the same reason.
    webAppLocation: 'westus2'
    storageLocation: location
    storageAccountName: storageAccountName
    webAppName: webAppName
    appServicePlanName: appServicePlanName
    appServicePlanSku: appServicePlanSku
    keyVaultName: keyVaultName
    appInsightsName: appInsightsName
    sharedResourceGroupName: sharedResourceGroupName
  }
}

output AZURE_RESOURCE_GROUP string = rg.name
output AZURE_STORAGE_ACCOUNT_NAME string = resources.outputs.storageAccountName
output SERVICE_API_URL string = resources.outputs.webAppUrl
output WEB_APP_PRINCIPAL_ID string = resources.outputs.webAppPrincipalId
