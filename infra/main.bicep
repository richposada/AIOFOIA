// FOIA Agent PoC — single-file Bicep IaC.
// Provisions: Log Analytics, Container Apps environment, Azure Container Registry,
// User-assigned managed identity, Storage Account (with foia-releases container),
// Azure OpenAI account + gpt-4o-mini deployment, and the foia-api Container App.
// Grants the managed identity Storage Blob Data Contributor + Storage Blob Delegator
// (for user-delegation SAS) plus AcrPull on the registry and Cognitive Services OpenAI User.

targetScope = 'resourceGroup'

// -------- Parameters --------
@minLength(1)
@maxLength(64)
@description('Name of the azd environment. Used to derive resource names.')
param environmentName string

@minLength(1)
@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Existing Azure AI Search endpoint URL (read-only consumer).')
param azureSearchEndpoint string

@description('Existing Azure AI Search index name.')
param azureSearchIndexName string

@secure()
@description('Existing Azure AI Search query/admin key.')
param azureSearchApiKey string

@description('OpenAI chat model deployment name.')
param openAiDeploymentName string = 'gpt-4o-mini'

@description('OpenAI model name to deploy.')
param openAiModelName string = 'gpt-4o-mini'

@description('OpenAI model version.')
param openAiModelVersion string = '2024-07-18'

@description('Image tag for the foia-api container. Set by azd to the built image reference.')
param foiaApiImage string = ''

// -------- Naming --------
var abbrs = loadJsonContent('./abbreviations.json').abbreviations
var resourceToken = uniqueString(subscription().id, resourceGroup().id, environmentName)
var tags = { 'azd-env-name': environmentName }

var logAnalyticsName     = '${abbrs.logAnalyticsWorkspace}${resourceToken}'
var containerEnvName     = '${abbrs.containerAppsEnvironment}${resourceToken}'
var containerAppName     = '${abbrs.containerApp}foia-api-${resourceToken}'
var registryName         = take('${abbrs.containerRegistry}${resourceToken}', 50)
var managedIdentityName  = '${abbrs.managedIdentity}foia-${resourceToken}'
var storageAccountName   = take('${abbrs.storageAccount}${resourceToken}', 24)
var openAiName           = '${abbrs.openAi}${resourceToken}'
var releasesContainer    = 'foia-releases'

// -------- Log Analytics --------
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
    name: logAnalyticsName
    location: location
    tags: tags
    properties: {
        sku: { name: 'PerGB2018' }
        retentionInDays: 30
    }
}

// -------- Managed identity --------
resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
    name: managedIdentityName
    location: location
    tags: tags
}

// -------- Container Registry --------
resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
    name: registryName
    location: location
    tags: tags
    sku: { name: 'Basic' }
    properties: {
        adminUserEnabled: false
    }
}

// AcrPull for the managed identity on the registry
resource acrPullRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
    name: guid(containerRegistry.id, managedIdentity.id, 'AcrPull')
    scope: containerRegistry
    properties: {
        principalId: managedIdentity.properties.principalId
        principalType: 'ServicePrincipal'
        // AcrPull
        roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    }
}

// -------- Storage --------
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
    name: storageAccountName
    location: location
    tags: tags
    sku: { name: 'Standard_LRS' }
    kind: 'StorageV2'
    properties: {
        allowBlobPublicAccess: false
        minimumTlsVersion: 'TLS1_2'
        supportsHttpsTrafficOnly: true
    }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
    parent: storage
    name: 'default'
    properties: {}
}

resource releasesContainerRes 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
    parent: blobService
    name: releasesContainer
    properties: {
        publicAccess: 'None'
    }
}

// Storage Blob Data Contributor for managed identity
resource storageBlobDataContributorRA 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
    name: guid(storage.id, managedIdentity.id, 'StorageBlobDataContributor')
    scope: storage
    properties: {
        principalId: managedIdentity.properties.principalId
        principalType: 'ServicePrincipal'
        // Storage Blob Data Contributor
        roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    }
}

// Storage Blob Delegator (for user-delegation SAS) — T029
resource storageBlobDelegatorRA 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
    name: guid(storage.id, managedIdentity.id, 'StorageBlobDelegator')
    scope: storage
    properties: {
        principalId: managedIdentity.properties.principalId
        principalType: 'ServicePrincipal'
        // Storage Blob Delegator
        roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'db58b8e5-c6ad-4a2a-8342-4190687cbf4a')
    }
}

// -------- Azure OpenAI --------
resource openAi 'Microsoft.CognitiveServices/accounts@2024-04-01-preview' = {
    name: openAiName
    location: location
    tags: tags
    kind: 'OpenAI'
    sku: { name: 'S0' }
    properties: {
        customSubDomainName: openAiName
        publicNetworkAccess: 'Enabled'
    }
}

resource openAiDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-04-01-preview' = {
    parent: openAi
    name: openAiDeploymentName
    sku: {
        name: 'GlobalStandard'
        capacity: 30
    }
    properties: {
        model: {
            format: 'OpenAI'
            name: openAiModelName
            version: openAiModelVersion
        }
    }
}

// Cognitive Services OpenAI User for managed identity
resource openAiUserRA 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
    name: guid(openAi.id, managedIdentity.id, 'CognitiveServicesOpenAIUser')
    scope: openAi
    properties: {
        principalId: managedIdentity.properties.principalId
        principalType: 'ServicePrincipal'
        // Cognitive Services OpenAI User
        roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd')
    }
}

// -------- Container Apps environment --------
resource containerEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
    name: containerEnvName
    location: location
    tags: tags
    properties: {
        appLogsConfiguration: {
            destination: 'log-analytics'
            logAnalyticsConfiguration: {
                customerId: logAnalytics.properties.customerId
                sharedKey: logAnalytics.listKeys().primarySharedKey
            }
        }
    }
}

// -------- Container App --------
var apiImage = empty(foiaApiImage) ? 'mcr.microsoft.com/k8se/quickstart:latest' : foiaApiImage

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
    name: containerAppName
    location: location
    tags: union(tags, { 'azd-service-name': 'foia-api' })
    identity: {
        type: 'UserAssigned'
        userAssignedIdentities: {
            '${managedIdentity.id}': {}
        }
    }
    properties: {
        managedEnvironmentId: containerEnv.id
        configuration: {
            ingress: {
                external: true
                targetPort: 8080
                transport: 'auto'
                allowInsecure: false
            }
            registries: [
                {
                    server: containerRegistry.properties.loginServer
                    identity: managedIdentity.id
                }
            ]
            secrets: [
                {
                    name: 'azure-search-api-key'
                    value: azureSearchApiKey
                }
            ]
        }
        template: {
            containers: [
                {
                    name: 'foia-api'
                    image: apiImage
                    resources: {
                        cpu: json('0.5')
                        memory: '1Gi'
                    }
                    env: [
                        { name: 'ASPNETCORE_URLS',                 value: 'http://+:8080' }
                        { name: 'AzureOpenAI__Endpoint',           value: openAi.properties.endpoint }
                        { name: 'AzureOpenAI__Deployment',         value: openAiDeploymentName }
                        { name: 'AzureSearch__Endpoint',           value: azureSearchEndpoint }
                        { name: 'AzureSearch__IndexName',          value: azureSearchIndexName }
                        { name: 'AzureSearch__ApiKey',             secretRef: 'azure-search-api-key' }
                        { name: 'AzureBlobStorage__AccountName',   value: storage.name }
                        { name: 'AzureBlobStorage__ContainerName', value: releasesContainer }
                        { name: 'AZURE_CLIENT_ID',                 value: managedIdentity.properties.clientId }
                    ]
                }
            ]
            scale: {
                minReplicas: 1
                maxReplicas: 1
            }
        }
    }
    dependsOn: [
        acrPullRoleAssignment
        storageBlobDataContributorRA
        storageBlobDelegatorRA
        openAiUserRA
    ]
}

// -------- Outputs (consumed by azd) --------
output AZURE_LOCATION string = location
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = containerRegistry.properties.loginServer
output AZURE_CONTAINER_REGISTRY_NAME string = containerRegistry.name
output AZURE_CONTAINER_APP_NAME string = containerApp.name
output AZURE_CONTAINER_APP_FQDN string = containerApp.properties.configuration.ingress.fqdn
output AZURE_OPENAI_ENDPOINT string = openAi.properties.endpoint
output AZURE_OPENAI_DEPLOYMENT string = openAiDeploymentName
output AZURE_STORAGE_ACCOUNT_NAME string = storage.name
output AZURE_STORAGE_CONTAINER_NAME string = releasesContainer
output AZURE_MANAGED_IDENTITY_CLIENT_ID string = managedIdentity.properties.clientId
output SERVICE_FOIA_API_IDENTITY_PRINCIPAL_ID string = managedIdentity.properties.principalId
