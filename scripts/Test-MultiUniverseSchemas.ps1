param([string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$manifestSchema = Join-Path $RepositoryRoot 'schemas/nap-manifest-v2.schema.json'
$profileSchema = Join-Path $RepositoryRoot 'schemas/nap-universe-profile-v1.schema.json'
$script:checks = 0

function Assert-Schema($value, [string] $schema, [bool] $expected, [string] $name) {
    $json = ConvertTo-Json -InputObject $value -Depth 20 -Compress
    $actual = Test-Json -Json $json -SchemaFile $schema -ErrorAction SilentlyContinue
    if ($actual -ne $expected) { throw "Schema check failed: $name (expected $expected, got $actual)." }
    $script:checks++
}

function New-Manifest {
    return @{
        schema_version = 2; universe_id = 'nimroel'; asset_id = 'portrait_example_001'
        asset_type = 'portrait'; production_profile = 'portrait_npc'; classification = @{}
    }
}

Assert-Schema (New-Manifest) $manifestSchema $true 'portrait has no universal required dimensions'
$generic = New-Manifest
$generic.universe_id = 'star_trek'
$generic.asset_type = 'future_type'
$generic.production_profile = 'future_profile'
$generic.classification = @{ faction = 'example'; ship = 'example'; department = 'example'; rank = 'example' }
Assert-Schema $generic $manifestSchema $true 'hypothetical generic manifest, without creating another universe profile'

foreach ($field in @('schema_version', 'universe_id', 'asset_id', 'asset_type', 'production_profile', 'classification')) {
    $value = New-Manifest
    $value.Remove($field)
    Assert-Schema $value $manifestSchema $false "missing $field"
    $value = New-Manifest
    $value[$field] = $null
    Assert-Schema $value $manifestSchema $false "null $field"
}
foreach ($version in @(1, 3, '2', 2.5)) {
    $value = New-Manifest
    $value.schema_version = $version
    Assert-Schema $value $manifestSchema $false "wrong version $version"
}
foreach ($field in @('universe_id', 'asset_type', 'production_profile')) {
    foreach ($invalid in @('', '   ', 'Upper', 'two words', 'bad-hyphen', 'bad__name', "name`n", ('a' * 65))) {
        $value = New-Manifest
        $value[$field] = $invalid
        Assert-Schema $value $manifestSchema $false "invalid $field identifier"
    }
    $value = New-Manifest
    $value[$field] = 'a' * 64
    Assert-Schema $value $manifestSchema $true "$field length 64"
}
foreach ($invalid in @('', '   ', 'portrait_example_000', 'portrait_example_01', 'portrait_001',
    'Portrait_example_001', 'nimroel:portrait_example_001', "portrait_example_001`n", ('p_' + ('a' * 91) + '_001'))) {
    $value = New-Manifest
    $value.asset_id = $invalid
    Assert-Schema $value $manifestSchema $false 'invalid asset_id'
}
$value = New-Manifest
$value.asset_id = 'p_' + ('a' * 90) + '_001'
Assert-Schema $value $manifestSchema $true 'asset_id length 96'
$value.asset_id = 'portrait_example_999'
Assert-Schema $value $manifestSchema $true 'asset sequence 999'
$value = New-Manifest
$value.extra = 'unexpected'
Assert-Schema $value $manifestSchema $false 'additional root property'
foreach ($classification in @(@{ 'Bad Key' = 'value' }, @{ key = '   ' }, @{ key = $null }, @{ key = 1 }, @('not_object'))) {
    $value = New-Manifest
    $value.classification = $classification
    Assert-Schema $value $manifestSchema $false 'invalid classification shape'
}

function New-Profile {
    return Get-Content -LiteralPath (Join-Path $RepositoryRoot 'config/universes/nimroel/profile.json') -Raw | ConvertFrom-Json -AsHashtable
}
Assert-Schema (New-Profile) $profileSchema $true 'real Nimroel profile'
foreach ($version in @(1.0, 1e0)) {
    $value = New-Profile
    $value.schema_version = $version
    Assert-Schema $value $profileSchema $true 'mathematically integer profile version'
}
$value = @{
    schema_version = 1; universe_id = 'test_universe'; display_name = 'Human Name'
    classification_dimensions = @('custom_dimension')
    asset_rules = @(@{ asset_type = 'custom_type'; production_profile = 'custom_profile';
        allowed_classification = @('custom_dimension'); required_classification = @() })
}
Assert-Schema $value $profileSchema $true 'generic profile schema accepts arbitrary dimensions'
foreach ($field in @('schema_version', 'universe_id', 'display_name', 'classification_dimensions', 'asset_rules')) {
    $value = New-Profile
    $value.Remove($field)
    Assert-Schema $value $profileSchema $false "missing profile $field"
}
foreach ($field in @('asset_type', 'production_profile', 'allowed_classification', 'required_classification')) {
    $value = New-Profile
    $value.asset_rules[0].Remove($field)
    Assert-Schema $value $profileSchema $false "missing rule $field"
}
foreach ($mode in @('unknown_root', 'unknown_rule', 'blank_name', 'wrong_version', 'bad_id',
    'duplicate_dimensions', 'bad_dimension', 'duplicate_allowed', 'duplicate_required', 'duplicate_rule')) {
    $value = New-Profile
    switch ($mode) {
        'unknown_root' { $value.extra = 1 }
        'unknown_rule' { $value.asset_rules[0].extra = 1 }
        'blank_name' { $value.display_name = '   ' }
        'wrong_version' { $value.schema_version = 2 }
        'bad_id' { $value.universe_id = 'Nimroel' }
        'duplicate_dimensions' { $value.classification_dimensions += 'culture' }
        'bad_dimension' { $value.classification_dimensions += 'Bad' }
        'duplicate_allowed' { $value.asset_rules[0].allowed_classification += 'culture' }
        'duplicate_required' { $value.asset_rules[0].required_classification += 'culture' }
        'duplicate_rule' { $value.asset_rules += $value.asset_rules[0].Clone() }
    }
    Assert-Schema $value $profileSchema $false "invalid profile $mode"
}

Write-Output "JSON Schema: $script:checks checks passed."
