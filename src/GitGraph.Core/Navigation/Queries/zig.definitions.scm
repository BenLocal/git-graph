; Definitions for the index; see csharp.definitions.scm for the captures. Zig names a type by assigning it to a
; constant (`const Point = struct { ... };`), so those patterns come before the plain constant one. Only `const` and
; `var` declare: `x += 1` and `_ = x` are variable_declarations too. Imports are file-local names that zig.locals.scm
; resolves, so they stay out of the index. Zig has no inheritance, so there are no @impl captures.

(variable_declaration "const" . (identifier) @name (struct_declaration)) @definition.struct
(variable_declaration "const" . (identifier) @name (union_declaration)) @definition.struct
(variable_declaration "const" . (identifier) @name (enum_declaration)) @definition.enum
(variable_declaration "const" . (identifier) @name (opaque_declaration)) @definition.type
(variable_declaration "const" . (identifier) @name (error_set_declaration)) @definition.type
(function_declaration name: (identifier) @name) @definition.function
(enum_declaration (container_field name: (identifier) @name (#match? @name ".")) @definition.constant)
(error_set_declaration (identifier) @name @definition.constant)
; An empty container (`opaque {}`) parses with an unnamed field.
(container_field name: (identifier) @name (#match? @name ".")) @definition.field

; Constants and variables of a file or a container, not of a function body.
((source_file (variable_declaration "const" . (identifier) @name) @definition.constant)
  (#not-match? @definition.constant "^[^=]*=\\s*@(import|cImport)\\b"))
((struct_declaration (variable_declaration "const" . (identifier) @name) @definition.constant)
  (#not-match? @definition.constant "^[^=]*=\\s*@(import|cImport)\\b"))
((union_declaration (variable_declaration "const" . (identifier) @name) @definition.constant)
  (#not-match? @definition.constant "^[^=]*=\\s*@(import|cImport)\\b"))
((enum_declaration (variable_declaration "const" . (identifier) @name) @definition.constant)
  (#not-match? @definition.constant "^[^=]*=\\s*@(import|cImport)\\b"))
((opaque_declaration (variable_declaration "const" . (identifier) @name) @definition.constant)
  (#not-match? @definition.constant "^[^=]*=\\s*@(import|cImport)\\b"))
(source_file (variable_declaration "var" . (identifier) @name) @definition.variable)
(struct_declaration (variable_declaration "var" . (identifier) @name) @definition.variable)
