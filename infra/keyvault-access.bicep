// Grants the web app's managed identity secret get/list on the shared Key Vault (access-policy mode).
// Name 'add' appends to the vault's existing policies — the other Po* identities depend on them.

@description('Key Vault in this (PoShared) resource group')
param keyVaultName string

@description('Principal ID of the identity to grant secret get/list')
param principalId string

@description('Tenant that owns the principal')
param tenantId string

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource accessPolicy 'Microsoft.KeyVault/vaults/accessPolicies@2023-07-01' = {
  parent: keyVault
  name: 'add'
  properties: {
    accessPolicies: [
      {
        tenantId: tenantId
        objectId: principalId
        permissions: {
          secrets: ['get', 'list']
        }
      }
    ]
  }
}
