; Same-file resolution of locals: @local.scope nodes, @local.definition identifiers, and @local.member names after
; a dot, which never refer to a local.

[
  (method_declaration)
  (constructor_declaration)
  (local_function_statement)
  (lambda_expression)
  (anonymous_method_expression)
  (accessor_declaration)
  (block)
  (for_statement)
  (foreach_statement)
  (using_statement)
  (catch_clause)
  (switch_section)
] @local.scope

(parameter name: (identifier) @local.definition)
(variable_declarator name: (identifier) @local.definition)
(foreach_statement left: (identifier) @local.definition)
(catch_declaration name: (identifier) @local.definition)
(declaration_pattern name: (identifier) @local.definition)
(local_function_statement name: (identifier) @local.definition)
(lambda_expression parameters: (implicit_parameter) @local.definition)

(member_access_expression name: (identifier) @local.member)
