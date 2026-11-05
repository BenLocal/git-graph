; Same-file resolution of locals; see csharp.locals.scm for the captures. Property names are members.

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

(formal_parameters (identifier) @local.definition)
(formal_parameters (assignment_pattern left: (identifier) @local.definition))
(formal_parameters (rest_pattern (identifier) @local.definition))
(arrow_function parameter: (identifier) @local.definition)
(variable_declarator name: (identifier) @local.definition)
(object_pattern (shorthand_property_identifier_pattern) @local.definition)
(object_pattern (pair_pattern value: (identifier) @local.definition))
(array_pattern (identifier) @local.definition)
(for_in_statement left: (identifier) @local.definition)
(catch_clause parameter: (identifier) @local.definition)
(function_declaration name: (identifier) @local.definition)
(class_declaration name: (identifier) @local.definition)
(import_clause (identifier) @local.definition)
(import_specifier alias: (identifier) @local.definition)
(import_specifier name: (identifier) @local.definition)
(namespace_import (identifier) @local.definition)

(property_identifier) @local.member
