; Same-file resolution of locals: @local.scope nodes, @local.definition identifiers, and @local.member names after
; a dot, which never refer to a local.

[
  (method_declaration)
  (constructor_declaration)
  (compact_constructor_declaration)
  (lambda_expression)
  (block)
  (for_statement)
  (enhanced_for_statement)
  (try_with_resources_statement)
  (catch_clause)
  (switch_block_statement_group)
] @local.scope

(formal_parameter name: (identifier) @local.definition)
(spread_parameter (variable_declarator name: (identifier) @local.definition))
(local_variable_declaration declarator: (variable_declarator name: (identifier) @local.definition))
(enhanced_for_statement name: (identifier) @local.definition)
(catch_formal_parameter name: (identifier) @local.definition)
(resource name: (identifier) @local.definition)
(instanceof_expression name: (identifier) @local.definition)
(lambda_expression parameters: (identifier) @local.definition)
(inferred_parameters (identifier) @local.definition)

(field_access field: (identifier) @local.member)
(method_invocation object: (_) name: (identifier) @local.member)
