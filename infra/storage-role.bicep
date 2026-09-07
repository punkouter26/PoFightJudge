// Storage Table Data Contributor + Storage Blob Data Contributor for the web app's managed identity.
// Assignment names are seeded with principalId (immutable per principal) so a delete-and-recreate of the
// site never collides with a stale assignment (RoleAssignmentUpdateNotPermitted).

@description('Storage account in this resource group')
param storageAccountName string

@description('Principal ID of the web app managed identity')
param principalId string

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
}

// Built-in roles
var tableDataContributor = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
var blobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource tableRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, principalId, tableDataContributor)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', tableDataContributor)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

resource blobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, principalId, blobDataContributor)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributor)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
