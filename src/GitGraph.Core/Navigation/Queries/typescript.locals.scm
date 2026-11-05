; Same-file resolution of locals, shared by .ts and .tsx; see csharp.locals.scm for the captures.

[
  (program)
  (function_declaration)
  (generator_function_declaration)
  (function_expression)
  (arrow_function)
  (method_definition)
  (statement_block)
  (for_statement)
  (for_in_statement)
  (catch_clause)
] @local.scope

(required_parameter pattern: (identifier) @local.definition)
(optional_parameter pattern: (identifier) @local.definition)
(arrow_function parameter: (identifier) @local.definition)
(variable_declarator name: (identifier) @local.definition)
(object_pattern (shorthand_property_identifier_pattern) @local.definition)
(object_pattern (pair_pattern value: (identifier) @local.definition))
(array_pattern (identifier) @local.definition)
(for_in_statement left: (identifier) @local.definition)
(catch_clause parameter: (identifier) @local.definition)
(function_declaration name: (identifier) @local.definition)
(import_clause (identifier) @local.definition)
(import_specifier alias: (identifier) @local.definition)
(import_specifier name: (identifier) @local.definition)
(namespace_import (identifier) @local.definition)

(property_identifier) @local.member
