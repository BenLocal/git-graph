; Same-file resolution of locals; see csharp.locals.scm for the captures. Field names (after `.` or `->`, in
; struct bodies and designated initializers) are field_identifier nodes and never a local.

[
  (function_definition)
  (compound_statement)
  (for_statement)
] @local.scope

(parameter_declaration declarator: (identifier) @local.definition)
(parameter_declaration declarator: (_ declarator: (identifier) @local.definition))
(parameter_declaration declarator: (_ declarator: (_ declarator: (identifier) @local.definition)))
(parameter_declaration declarator: (function_declarator declarator: (parenthesized_declarator (pointer_declarator declarator: (identifier) @local.definition))))
(declaration declarator: (identifier) @local.definition)
(declaration declarator: (_ declarator: (identifier) @local.definition))
(declaration declarator: (_ declarator: (_ declarator: (identifier) @local.definition)))

(field_identifier) @local.member
