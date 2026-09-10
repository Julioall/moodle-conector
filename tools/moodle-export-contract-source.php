<?php

/**
 * Export Moodle External Function descriptions to the connector's source
 * manifest format.
 *
 * This is a standalone CLI exporter. It is run against an existing Moodle
 * checkout and does not install a plugin, add a web service, execute an
 * external function, or emit credentials. The generated source still needs
 * human review, the connector's hash preparation, and live homologation
 * before any contract can be marked verified.
 */

declare(strict_types=1);

if (PHP_SAPI !== 'cli') {
    fwrite(STDERR, "This exporter must be run from the Moodle CLI.\n");
    exit(2);
}

define('CLI_SCRIPT', true);
define('NO_OUTPUT_BUFFERING', true);

function fail_export(string $message): never {
    fwrite(STDERR, "Moodle contract source export failed: {$message}\n");
    exit(1);
}

function usage(): never {
    fwrite(STDERR, <<<'USAGE'
Usage:
  php tools/moodle-export-contract-source.php \
    --moodle-root=/var/www/moodle \
    --output=/secure/export/moodle-contract-source.json \
    [--source=controlled-export://fieg/2026-09-10] \
    [--status=missing|invalid|stale|conflicting|verified] \
    [--function=core_course_get_courses_by_field]

  php tools/moodle-export-contract-source.php \
    --moodle-root=/path/to/moodle-source \
    --source-only \
    --output=/secure/export/moodle-contract-source.json \
    [--source=official-moodle-source://5.1.2] \
    [--status=missing|invalid|stale|conflicting] \
    [--function=core_course_get_courses_by_field]

The default mode reads the installed external_functions registry and invokes
only parameter/return description methods. The --source-only mode reads
db/services.php and the external description classes from a Moodle source
checkout; it is a code snapshot, not proof of the installed database, enabled
plugins, capabilities, function versions, or live behaviour. It never invokes
an external function. Use status=verified only after independent review and
live homologation, and never with --source-only.
USAGE
    );
    exit(2);
}

function option_value(array $options, string $name, bool $required = false): ?string {
    $value = $options[$name] ?? null;
    if (is_array($value)) {
        $value = end($value);
    }
    $value = is_string($value) ? trim($value) : null;
    if ($required && ($value === null || $value === '')) {
        fail_export("missing --{$name}");
    }
    return $value === '' ? null : $value;
}

function append_warning(array &$warnings, string $message): void {
    if (!in_array($message, $warnings, true)) {
        $warnings[] = $message;
    }
}

function add_description_metadata(array &$schema, object $description): void {
    if (property_exists($description, 'desc') && is_string($description->desc) && $description->desc !== '') {
        $schema['description'] = $description->desc;
    }

    if (property_exists($description, 'default')) {
        // Moodle's description objects expose the authoritative default value.
        // Keeping an explicit null is safer than dropping a declared default.
        $schema['default'] = $description->default;
    }

    if (property_exists($description, 'allownull') && $description->allownull) {
        $type = $schema['type'] ?? 'object';
        if (is_string($type)) {
            $schema['type'] = [$type, 'null'];
        } elseif (is_array($type) && !in_array('null', $type, true)) {
            $schema['type'][] = 'null';
        }
    }
}

function scalar_json_type(mixed $moodleType): string {
    $type = strtolower((string)$moodleType);
    return match ($type) {
        'int', 'integer', 'param_int', 'param_integer' => 'integer',
        'float', 'double', 'number', 'param_float' => 'number',
        'bool', 'boolean', 'param_bool' => 'boolean',
        'array', 'param_array' => 'array',
        default => 'string',
    };
}

/**
 * @return array{schema: array<string,mixed>, warnings: list<string>, acceptsFiles: bool}
 */
function description_to_schema(?object $description, string $path, array &$warnings, string $direction): array {
    if ($description === null) {
        return [
            'schema' => ['type' => 'null'],
            'warnings' => $warnings,
            'acceptsFiles' => false,
        ];
    }

    $class = get_class($description);
    $schema = [];
    $containsFiles = false;

    if ($description instanceof \core_external\external_function_parameters ||
        $description instanceof \core_external\external_single_structure) {
        $properties = [];
        $required = [];
        foreach ($description->keys as $key => $child) {
            if (!$child instanceof \core_external\external_description) {
                append_warning($warnings, "{$path}.{$key}: unmapped child description");
                continue;
            }
            $childResult = description_to_schema($child, "{$path}.{$key}", $warnings, $direction);
            $properties[(string)$key] = $childResult['schema'];
            $containsFiles = $containsFiles || $childResult['acceptsFiles'];
            if (property_exists($child, 'required') && $child->required) {
                $required[] = (string)$key;
            }
        }
        $schema = [
            'type' => 'object',
            'properties' => $properties,
            'additionalProperties' => false,
        ];
        if ($required !== []) {
            $schema['required'] = $required;
        }
        add_description_metadata($schema, $description);
    } elseif ($description instanceof \core_external\external_multiple_structure &&
        !($description instanceof \core_external\external_files) &&
        !($description instanceof \core_external\external_warnings)) {
        $child = $description->content ?? null;
        $childResult = $child instanceof \core_external\external_description
            ? description_to_schema($child, "{$path}[]", $warnings, $direction)
            : [
                'schema' => ['type' => 'object'],
                'acceptsFiles' => false,
            ];
        if (!$child instanceof \core_external\external_description) {
            append_warning($warnings, "{$path}: multiple structure content is not an external description");
        }
        $schema = [
            'type' => 'array',
            'items' => $childResult['schema'],
        ];
        $containsFiles = $childResult['acceptsFiles'];
        add_description_metadata($schema, $description);
    } elseif ($description instanceof \core_external\external_files) {
        $schema = [
            'type' => 'array',
            'items' => [
                'type' => 'object',
                'additionalProperties' => true,
            ],
            'x-moodle-description-type' => 'external_files',
        ];
        $containsFiles = true;
        add_description_metadata($schema, $description);
    } elseif ($description instanceof \core_external\external_warnings) {
        $schema = [
            'type' => 'array',
            'items' => [
                'type' => 'object',
                'properties' => [
                    'item' => ['type' => ['string', 'null']],
                    'itemid' => ['type' => ['integer', 'null']],
                    'warningcode' => ['type' => ['string', 'null']],
                    'message' => ['type' => ['string', 'null']],
                ],
                'additionalProperties' => true,
            ],
            'x-moodle-description-type' => 'external_warnings',
        ];
        add_description_metadata($schema, $description);
    } elseif ($description instanceof \core_external\external_value) {
        $schema = ['type' => scalar_json_type($description->type)];
        add_description_metadata($schema, $description);
    } else {
        append_warning($warnings, "{$path}: unsupported description class {$class}");
        $schema = [
            'type' => 'object',
            'additionalProperties' => true,
            'x-moodle-unmapped-description-class' => $class,
        ];
    }

    if ($containsFiles) {
        $schema['x-moodle-file-direction'] = $direction;
    }

    return [
        'schema' => $schema,
        'warnings' => $warnings,
        'acceptsFiles' => $containsFiles,
    ];
}

function normalize_effect(mixed $type): string {
    $effect = strtolower(trim((string)$type));
    return in_array($effect, ['read', 'write'], true) ? $effect : 'unknown';
}

function capabilities_from_row(object $row): array {
    $raw = $row->capabilities ?? '';
    if (is_array($raw)) {
        return array_values(array_filter(array_map('strval', $raw), static fn(string $item): bool => trim($item) !== ''));
    }
    $parts = preg_split('/[,\s]+/', (string)$raw, -1, PREG_SPLIT_NO_EMPTY);
    return array_values(array_map('strval', $parts ?: []));
}

function is_administrative(array $capabilities): bool {
    foreach ($capabilities as $capability) {
        if (in_array(strtolower($capability), ['moodle/site:config', 'moodle/site:doanything'], true)) {
            return true;
        }
    }
    return false;
}

/**
 * Bootstrap the public Moodle source tree without requiring a database.
 *
 * This is intentionally separate from the installed-export path below. A
 * source checkout can describe most parameter/return contracts, but it cannot
 * prove the external_functions rows, installed plugin versions, capabilities,
 * or site-specific configuration of a Moodle instance.
 */
function bootstrap_source_tree(string $sourceRoot): string {
    global $CFG;

    $documentRoot = is_file($sourceRoot . DIRECTORY_SEPARATOR . 'public' . DIRECTORY_SEPARATOR . 'version.php')
        ? $sourceRoot . DIRECTORY_SEPARATOR . 'public'
        : $sourceRoot;
    $documentRoot = realpath($documentRoot) ?: '';
    if ($documentRoot === '' || !is_file($documentRoot . DIRECTORY_SEPARATOR . 'version.php')) {
        fail_export("Moodle source tree does not contain version.php: {$sourceRoot}");
    }

    $CFG = new stdClass();
    $CFG->dirroot = $documentRoot;
    $CFG->libdir = $documentRoot . DIRECTORY_SEPARATOR . 'lib';
    $CFG->dataroot = sys_get_temp_dir() . DIRECTORY_SEPARATOR . 'moodle-contract-source-data';
    $CFG->cachedir = $CFG->dataroot . DIRECTORY_SEPARATOR . 'cache';
    $CFG->localcachedir = $CFG->dataroot . DIRECTORY_SEPARATOR . 'localcache';
    $CFG->tempdir = $CFG->dataroot . DIRECTORY_SEPARATOR . 'temp';
    $CFG->wwwroot = 'http://localhost';
    $CFG->admin = 'admin';
    $CFG->debug = 0;
    $CFG->debugdisplay = 0;
    $CFG->debugdeveloper = 0;
    $CFG->directorypermissions = 0770;
    $CFG->forced_plugin_settings = [];
    $CFG->defaultcity = '';
    $CFG->country = '';
    $CFG->lang = 'en';
    $CFG->calendartype = 'gregorian';
    $CFG->langotherroot = '';
    $CFG->langlocalroot = '';

    if (!defined('MOODLE_INTERNAL')) {
        define('MOODLE_INTERNAL', true);
    }
    if (!defined('CLI_SCRIPT')) {
        define('CLI_SCRIPT', true);
    }
    if (!defined('MUST_EXIST')) {
        define('MUST_EXIST', 1);
    }
    if (!defined('IGNORE_MISSING')) {
        define('IGNORE_MISSING', 0);
    }
    if (!defined('CACHE_DISABLE_ALL')) {
        define('CACHE_DISABLE_ALL', true);
    }
    if (!defined('SYSCONTEXTID')) {
        // A source-only bootstrap has no context table. This value is only a
        // compatibility placeholder for static external descriptions.
        define('SYSCONTEXTID', 1);
    }
    if (!defined('SITEID')) {
        // As above, this is never used to execute a Moodle function.
        define('SITEID', 1);
    }

    set_include_path($CFG->libdir . DIRECTORY_SEPARATOR . 'pear' . PATH_SEPARATOR . get_include_path());
    // The source tree may contain harmless notices while loading dynamic
    // descriptions. They must not corrupt the JSON document on stdout.
    error_reporting(E_ERROR | E_PARSE);

    require_once $CFG->libdir . DIRECTORY_SEPARATOR . 'setuplib.php';
    require_once $CFG->libdir . DIRECTORY_SEPARATOR . 'classes' . DIRECTORY_SEPARATOR . 'component.php';
    \core_component::register_autoloader();
    require_once $CFG->libdir . DIRECTORY_SEPARATOR . 'moodlelib.php';
    require_once $CFG->libdir . DIRECTORY_SEPARATOR . 'weblib.php';

    $release = '';
    require $documentRoot . DIRECTORY_SEPARATOR . 'version.php';
    if (!isset($release) || trim((string)$release) === '') {
        fail_export("Moodle source version.php does not define release: {$documentRoot}");
    }

    return trim((string)$release);
}

/**
 * Discover source-level service declarations.
 *
 * @return array<string, stdClass>
 */
function discover_source_rows(): array {
    $rows = [];
    foreach (\core_component::get_component_names(true, true) as $component) {
        $directory = \core_component::get_component_directory($component);
        if (!$directory) {
            continue;
        }
        $servicesFile = $directory . DIRECTORY_SEPARATOR . 'db' . DIRECTORY_SEPARATOR . 'services.php';
        if (!is_file($servicesFile)) {
            continue;
        }

        $functions = null;
        include $servicesFile;
        foreach (($functions ?? []) as $name => $definition) {
            $row = (object)$definition;
            $row->name = (string)$name;
            $row->component = $component;
            // Newer Moodle service declarations use execute_* methods and
            // omit methodname; the installed upgrade path persists execute.
            $row->methodname = $row->methodname ?? 'execute';
            $rows[$row->name] = $row;
        }
    }

    ksort($rows, SORT_NATURAL | SORT_FLAG_CASE);
    return $rows;
}

$options = getopt('', [
    'moodle-root:',
    'output:',
    'source::',
    'status::',
    'function::',
    'source-only',
]);
if ($options === false) {
    usage();
}

$moodleRoot = option_value($options, 'moodle-root', true);
$outputPath = option_value($options, 'output', true);
$source = option_value($options, 'source') ?? 'moodle-cli://external-function-export';
$status = strtolower(option_value($options, 'status') ?? 'missing');
$onlyFunction = option_value($options, 'function');
$sourceOnly = array_key_exists('source-only', $options);
if (!in_array($status, ['verified', 'missing', 'stale', 'conflicting', 'invalid'], true)) {
    fail_export('--status must be verified, missing, stale, conflicting, or invalid');
}
if ($sourceOnly && $status === 'verified') {
    fail_export('--source-only cannot emit verified contracts; export as missing/invalid and homologate against the live installation');
}

$moodleRoot = realpath($moodleRoot) ?: '';
$moodleVersion = '';
if ($sourceOnly) {
    if ($moodleRoot === '') {
        fail_export("Moodle source tree not found: {$moodleRoot}");
    }
    $moodleVersion = bootstrap_source_tree($moodleRoot);
    $rows = discover_source_rows();
    if ($rows === []) {
        fail_export("no db/services.php declarations found in Moodle source tree: {$moodleRoot}");
    }
} else {
    if ($moodleRoot === '' || !is_file($moodleRoot . DIRECTORY_SEPARATOR . 'config.php')) {
        fail_export("--moodle-root does not contain config.php: {$moodleRoot}");
    }

    require_once $moodleRoot . DIRECTORY_SEPARATOR . 'config.php';
    global $CFG, $DB;
    if (!isset($CFG, $DB)) {
        fail_export('Moodle config did not initialize CFG and DB.');
    }

    $moodleVersion = (string)($CFG->release ?? '');
    $rows = $DB->get_records('external_functions', null, 'name ASC');
}
$contracts = [];
$counts = [
    'discovered' => 0,
    'converted' => 0,
    'invalid' => 0,
];

foreach ($rows as $row) {
    $name = trim((string)($row->name ?? ''));
    if ($name === '' || ($onlyFunction !== null && !hash_equals(strtolower($onlyFunction), strtolower($name)))) {
        continue;
    }
    $counts['discovered']++;
    $warnings = [];
    $capabilities = capabilities_from_row($row);
    $adminOnly = is_administrative($capabilities);
    $contract = [
        'functionName' => $name,
        // The effect is normally declared in db/services.php, not in the
        // external_functions table. It is filled after external_function_info
        // loads that service definition below.
        'effect' => 'unknown',
        'component' => isset($row->component) ? (string)$row->component : null,
        'externalFunctionVersion' => isset($row->version) ? (string)$row->version : null,
        'moodleVersion' => $moodleVersion,
        'source' => $source,
        'requiredScopes' => ['moodle.read'],
        'status' => $status,
        'administrativeOnly' => $adminOnly,
    ];
    if ($adminOnly) {
        $contract['platformPermission'] = 'admin.view';
    }
    if ($capabilities !== []) {
        $contract['moodleCapabilities'] = $capabilities;
    }

    try {
        $info = \core_external\external_api::external_function_info($row, IGNORE_MISSING);
        if (!$info) {
            throw new RuntimeException('external_function_info returned no record');
        }
        // external_function_info() includes the type loaded from the
        // component's db/services.php. Reading $row->type alone silently
        // classified ordinary Moodle functions as unknown because that field
        // is not part of the external_functions table on supported releases.
        $effect = normalize_effect($info->type ?? $row->type ?? null);
        $contract['effect'] = $effect;
        $contract['requiredScopes'] = [$effect === 'write' ? 'moodle.write' : 'moodle.read'];
        if ($effect === 'unknown') {
            append_warning($warnings, 'external function effect is not declared as read or write');
        }
        $inputResult = description_to_schema($info->parameters_desc, $name . '.parameters', $warnings, 'input');
        $outputResult = description_to_schema($info->returns_desc, $name . '.returns', $warnings, 'output');
        $contract['inputSchema'] = $inputResult['schema'];
        $contract['outputSchema'] = $outputResult['schema'];
        $acceptsFiles = $inputResult['acceptsFiles'];
        $returnsFiles = $outputResult['acceptsFiles'];
        if ($acceptsFiles || $returnsFiles) {
            $contract['fileHandling'] = [
                'acceptsFiles' => $acceptsFiles,
                'returnsFiles' => $returnsFiles,
                'destination' => null,
                'allowedMimeTypes' => null,
            ];
        }
        if ($warnings !== []) {
            $contract['conversionWarnings'] = $warnings;
            if ($contract['status'] === 'verified') {
                $contract['status'] = 'invalid';
            }
        } else {
            $counts['converted']++;
        }
    } catch (Throwable $exception) {
        $counts['invalid']++;
        $contract['status'] = 'invalid';
        $contract['inputSchema'] = [
            'type' => 'object',
            'additionalProperties' => true,
            'x-moodle-export-error' => 'description_unavailable',
        ];
        $contract['outputSchema'] = [
            'type' => 'object',
            'additionalProperties' => true,
            'x-moodle-export-error' => 'description_unavailable',
        ];
        $contract['conversionWarnings'] = [
            'description export failed: ' . get_class($exception),
        ];
    }

    $contracts[] = $contract;
}

if ($counts['discovered'] === 0) {
    fail_export($onlyFunction === null
        ? 'no rows found in external_functions'
        : "function not found: {$onlyFunction}");
}

$document = [
    'schemaVersion' => 1,
    'source' => $source,
    'moodleVersion' => $moodleVersion,
    'generatedAtUtc' => gmdate('c'),
    'contracts' => $contracts,
];
$json = json_encode($document, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE);
if ($json === false) {
    fail_export('could not serialize the source manifest: ' . json_last_error_msg());
}

if ($outputPath === '-') {
    fwrite(STDOUT, $json . PHP_EOL);
} else {
    $outputDirectory = dirname($outputPath);
    if (!is_dir($outputDirectory) && !mkdir($outputDirectory, 0770, true) && !is_dir($outputDirectory)) {
        fail_export("could not create output directory: {$outputDirectory}");
    }
    $temporaryPath = tempnam($outputDirectory, '.moodle-contract-source-');
    if ($temporaryPath === false || file_put_contents($temporaryPath, $json . PHP_EOL, LOCK_EX) === false) {
        fail_export("could not write output: {$outputPath}");
    }
    if (!rename($temporaryPath, $outputPath)) {
        @unlink($temporaryPath);
        fail_export("could not publish output atomically: {$outputPath}");
    }
}

fwrite(STDERR, sprintf(
    "Moodle contract source export completed: discovered=%d; converted=%d; invalid=%d; status=%s\n",
    $counts['discovered'],
    $counts['converted'],
    $counts['invalid'],
    $status
));
