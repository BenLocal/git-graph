; Same-file resolution of locals; see csharp.locals.scm for the captures.

[
  (function_declaration)
  (method_declaration)
  (func_literal)
  (block)
  (if_statement)
  (for_statement)
  (expression_switch_statement)
  (type_switch_statement)
  (select_statement)
  (expression_case)
  (type_case)
  (default_case)
  (communication_case)
] @local.scope

(parameter_declaration name: (identifier) @local.definition)
(variadic_parameter_declaration name: (identifier) @local.definition)
(short_var_declaration left: (expression_list (identifier) @local.definition))
(var_spec name: (identifier) @local.definition)
(const_spec name: (identifier) @local.definition)
(range_clause left: (expression_list (identifier) @local.definition))
(type_switch_statement alias: (expression_list (identifier) @local.definition))

(selector_expression field: (field_identifier) @local.member)
