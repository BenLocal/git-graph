; Same-file resolution of locals; see csharp.locals.scm for the captures. Assignments anywhere in a function are
; the same variable as the first one, as in Python.

[
  (module)
  (class_definition)
  (function_definition)
  (lambda)
  (list_comprehension)
  (set_comprehension)
  (dictionary_comprehension)
  (generator_expression)
] @local.scope

(parameters (identifier) @local.definition)
(default_parameter name: (identifier) @local.definition)
(typed_parameter (identifier) @local.definition)
(typed_default_parameter name: (identifier) @local.definition)
(list_splat_pattern (identifier) @local.definition)
(dictionary_splat_pattern (identifier) @local.definition)
(lambda_parameters (identifier) @local.definition)
(assignment left: (identifier) @local.definition)
(assignment left: (pattern_list (identifier) @local.definition))
(assignment left: (tuple_pattern (identifier) @local.definition))
(for_statement left: (identifier) @local.definition)
(for_statement left: (pattern_list (identifier) @local.definition))
(for_in_clause left: (identifier) @local.definition)
(for_in_clause left: (pattern_list (identifier) @local.definition))
(as_pattern alias: (as_pattern_target (identifier) @local.definition))
(function_definition name: (identifier) @local.definition)
(class_definition name: (identifier) @local.definition)
(import_statement name: (dotted_name . (identifier) @local.definition))
(import_from_statement name: (dotted_name (identifier) @local.definition))
(aliased_import alias: (identifier) @local.definition)

(attribute attribute: (identifier) @local.member)
(keyword_argument name: (identifier) @local.member)
