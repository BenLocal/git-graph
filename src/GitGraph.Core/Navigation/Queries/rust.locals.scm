; Same-file resolution of locals; see csharp.locals.scm for the captures. A later `let` in the same block
; shadows an earlier one (NavLanguage.ShadowsInScope).

[
  (function_item)
  (closure_expression)
  (block)
  (for_expression)
  (match_arm)
] @local.scope

(parameter pattern: (identifier) @local.definition)
(let_declaration pattern: (identifier) @local.definition)
(let_declaration pattern: (tuple_pattern (identifier) @local.definition))
(let_condition pattern: (identifier) @local.definition)
(tuple_struct_pattern type: (_) (identifier) @local.definition)
(for_expression pattern: (identifier) @local.definition)
(closure_parameters (identifier) @local.definition)
