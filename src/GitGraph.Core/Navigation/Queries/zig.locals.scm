; Same-file resolution of locals; see csharp.locals.scm for the captures. An import (`const std = @import("std");`)
; is a local of the whole file. Payloads (`|item, i|`) belong to the loop, branch or catch that declares them.

[
  (source_file)
  (function_declaration)
  (block)
  (for_statement)
  (for_expression)
  (while_statement)
  (while_expression)
  (if_statement)
  (if_expression)
  (else_clause)
  (switch_case)
  (catch_expression)
  (errdefer_statement)
] @local.scope

((source_file (variable_declaration "const" . (identifier) @local.definition) @_import)
  (#match? @_import "^[^=]*=\\s*@(import|cImport)\\b"))
(function_declaration (parameters (parameter name: (identifier) @local.definition)))
(block (variable_declaration ["const" "var"] . (identifier) @local.definition))
(payload (identifier) @local.definition)
(block_label (identifier) @local.definition)

(field_expression member: (identifier) @local.member)
(container_field name: (identifier) @local.member)
