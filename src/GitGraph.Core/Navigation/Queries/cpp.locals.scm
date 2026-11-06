; Same-file resolution of locals; see csharp.locals.scm for the captures. Field names (after `.` or `->`, in class
; bodies and member initializers) are field_identifier nodes, and a name after `::` is never a local.

[
  (function_definition)
  (lambda_expression)
  (compound_statement)
  (for_statement)
  (for_range_loop)
  (if_statement)
  (switch_statement)
  (while_statement)
  (catch_clause)
] @local.scope

(parameter_declaration declarator: (identifier) @local.definition)
(parameter_declaration declarator: (_ declarator: (identifier) @local.definition))
(parameter_declaration declarator: (_ declarator: (_ declarator: (identifier) @local.definition)))
(parameter_declaration declarator: (reference_declarator (identifier) @local.definition))
(parameter_declaration declarator: (function_declarator declarator: (parenthesized_declarator (pointer_declarator declarator: (identifier) @local.definition))))
(optional_parameter_declaration declarator: (identifier) @local.definition)
(optional_parameter_declaration declarator: (_ declarator: (identifier) @local.definition))
(optional_parameter_declaration declarator: (reference_declarator (identifier) @local.definition))
(variadic_declarator (identifier) @local.definition)
(declaration declarator: (identifier) @local.definition)
(declaration declarator: (_ declarator: (identifier) @local.definition))
(declaration declarator: (_ declarator: (_ declarator: (identifier) @local.definition)))
(declaration declarator: (init_declarator declarator: (reference_declarator (identifier) @local.definition)))
(for_range_loop declarator: (identifier) @local.definition)
(for_range_loop declarator: (reference_declarator (identifier) @local.definition))
(structured_binding_declarator (identifier) @local.definition)

(field_identifier) @local.member
(qualified_identifier name: (identifier) @local.member)
