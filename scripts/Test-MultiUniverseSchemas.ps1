param([string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$manifestSchema = Join-Path $RepositoryRoot 'schemas/nap-manifest-v2.schema.json'
$profileSchema = Join-Path $RepositoryRoot 'schemas/nap-universe-profile-v1.schema.json'
$profileV2Schema = Join-Path $RepositoryRoot 'schemas/nap-universe-profile-v2.schema.json'
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
    return Get-Content -LiteralPath (Join-Path $RepositoryRoot 'test-data/phase2/universe-profile-v1/profile.json') -Raw | ConvertFrom-Json -AsHashtable
}
Assert-Schema (New-Profile) $profileSchema $true 'historical Nimroel Profile v1 fixture'
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

$value = New-Profile
$value.asset_rules[0].package_files = @()
Assert-Schema $value $profileSchema $false 'Profile v1 must reject package_files'

function New-ProfileV2 {
    return Get-Content -LiteralPath (Join-Path $RepositoryRoot 'config/universes/nimroel/profile.json') -Raw | ConvertFrom-Json -AsHashtable
}

Assert-Schema (New-ProfileV2) $profileV2Schema $true 'real Nimroel Profile v2'
Assert-Schema (New-ProfileV2) $profileSchema $false 'Profile v2 is not interpreted as v1'
Assert-Schema (New-Profile) $profileV2Schema $false 'Profile v1 is not interpreted as v2'
foreach ($version in @(2.0, 2e0)) {
    $value = New-ProfileV2
    $value.schema_version = $version
    Assert-Schema $value $profileV2Schema $true 'mathematically integer Profile v2 version'
}
foreach ($version in @(1, 3, 2.5, '2', $null)) {
    $value = New-ProfileV2
    $value.schema_version = $version
    Assert-Schema $value $profileV2Schema $false 'invalid Profile v2 version'
}
foreach ($field in @('schema_version', 'universe_id', 'display_name', 'classification_dimensions', 'asset_rules')) {
    $value = New-ProfileV2
    $value.Remove($field)
    Assert-Schema $value $profileV2Schema $false "missing Profile v2 $field"
}
foreach ($field in @('asset_type', 'production_profile', 'allowed_classification', 'required_classification', 'package_files')) {
    $value = New-ProfileV2
    $value.asset_rules[0].Remove($field)
    Assert-Schema $value $profileV2Schema $false "missing Profile v2 asset rule $field"
}
foreach ($field in @('role', 'suffix', 'extension', 'required', 'content_validator')) {
    $value = New-ProfileV2
    $value.asset_rules[0].package_files[0].Remove($field)
    Assert-Schema $value $profileV2Schema $false "missing package file $field"
}
foreach ($mode in @('unknown_root', 'unknown_rule', 'unknown_file', 'null_files', 'object_files', 'null_file',
    'null_role', 'null_suffix', 'null_extension', 'null_required', 'string_required', 'number_required', 'number_validator',
    'duplicate_file', 'duplicate_rule', 'duplicate_dimensions', 'duplicate_allowed', 'duplicate_required', 'blank_name', 'bad_id', 'universal_manifest')) {
    $value = New-ProfileV2
    switch ($mode) {
        'unknown_root' { $value.extra = 1 }
        'unknown_rule' { $value.asset_rules[0].extra = 1 }
        'unknown_file' { $value.asset_rules[0].package_files[0].extra = 1 }
        'null_files' { $value.asset_rules[0].package_files = $null }
        'object_files' { $value.asset_rules[0].package_files = @{} }
        'null_file' { $value.asset_rules[0].package_files += $null }
        'null_role' { $value.asset_rules[0].package_files[0].role = $null }
        'null_suffix' { $value.asset_rules[0].package_files[0].suffix = $null }
        'null_extension' { $value.asset_rules[0].package_files[0].extension = $null }
        'null_required' { $value.asset_rules[0].package_files[0].required = $null }
        'string_required' { $value.asset_rules[0].package_files[0].required = 'true' }
        'number_required' { $value.asset_rules[0].package_files[0].required = 1 }
        'number_validator' { $value.asset_rules[0].package_files[0].content_validator = 1 }
        'duplicate_file' { $value.asset_rules[0].package_files += $value.asset_rules[0].package_files[0].Clone() }
        'duplicate_rule' { $value.asset_rules += $value.asset_rules[0].Clone() }
        'duplicate_dimensions' { $value.classification_dimensions += 'culture' }
        'duplicate_allowed' { $value.asset_rules[0].allowed_classification += 'culture' }
        'duplicate_required' { $value.asset_rules[0].required_classification += 'culture' }
        'blank_name' { $value.display_name = '   ' }
        'bad_id' { $value.universe_id = 'Nimroel' }
        'universal_manifest' { $value.asset_rules[0].package_files[0].suffix = '_manifest'; $value.asset_rules[0].package_files[0].extension = '.json' }
    }
    Assert-Schema $value $profileV2Schema $false "invalid Profile v2 $mode"
}
foreach ($field in @('role', 'content_validator')) {
    foreach ($invalid in @('', '   ', 'Upper', 'two words', 'bad__name', 'bad_', "name`n", ('a' * 65))) {
        $value = New-ProfileV2
        $value.asset_rules[0].package_files[0][$field] = $invalid
        Assert-Schema $value $profileV2Schema $false "invalid package file $field"
    }
    $value = New-ProfileV2
    $value.asset_rules[0].package_files[0][$field] = 'a' * 64
    Assert-Schema $value $profileV2Schema $true "package file $field length 64"
}
foreach ($suffix in @('', '_prompt', '_visual_identity', ('_' + ('a' * 64)))) {
    $value = New-ProfileV2
    $value.asset_rules[0].package_files = @($value.asset_rules[0].package_files[0])
    $value.asset_rules[0].package_files[0].suffix = $suffix
    Assert-Schema $value $profileV2Schema $true 'valid suffix'
}
foreach ($suffix in @('prompt', '__prompt', '_Prompt', '_prompt_', '_two words', '../x', '_a/b', '_a\b', '_a:b', '_a.b', "_prompt`n", ('_' + ('a' * 65)))) {
    $value = New-ProfileV2
    $value.asset_rules[0].package_files[0].suffix = $suffix
    Assert-Schema $value $profileV2Schema $false 'invalid suffix'
}
foreach ($extension in @('.png', '.md', '.json', '.webp', '.wav', '.ogg', '.tar.gz', '.7z')) {
    $value = New-ProfileV2
    $value.asset_rules[0].package_files[0].extension = $extension
    Assert-Schema $value $profileV2Schema $true 'valid extension'
}
foreach ($extension in @('png', '.', '..', '.PNG', '.bad extension', '../png', '.a/b', '.a\b', '.a:b', '.a..b', '.png.', ".png`n", '.é', '.a*', '.a?', '.a|', '.a<', '.a>', '.a"')) {
    $value = New-ProfileV2
    $value.asset_rules[0].package_files[0].extension = $extension
    Assert-Schema $value $profileV2Schema $false 'invalid extension'
}
$value = New-ProfileV2
$value.universe_id = 'test_universe'
$value.asset_rules[0].package_files = @(@{ role = 'audio_master'; suffix = ''; extension = '.wav'; required = $false; content_validator = 'future_audio_validator' })
Assert-Schema $value $profileV2Schema $true 'generic future role and validator, optional file'
$value.asset_rules[0].package_files[0].content_validator = $null
Assert-Schema $value $profileV2Schema $true 'explicit null content validator'
$value.asset_rules[0].package_files = @()
Assert-Schema $value $profileV2Schema $true 'empty package files are allowed'
# Different objects with repeated roles or filenames require runtime semantic checks.
$value = New-ProfileV2
$value.asset_rules[0].package_files[1].role = 'master'
Assert-Schema $value $profileV2Schema $true 'role uniqueness belongs to C#'
$value = New-ProfileV2
$value.asset_rules[0].package_files[1].suffix = ''
$value.asset_rules[0].package_files[1].extension = '.png'
Assert-Schema $value $profileV2Schema $true 'filename collision belongs to C#'

Write-Output "JSON Schema: $script:checks checks passed."
