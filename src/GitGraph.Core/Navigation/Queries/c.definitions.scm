; Definitions for the index; see csharp.definitions.scm for the captures. C has no supertypes.
; Declarators nest (pointers, arrays, initializers), and queries can't recurse, so each depth is spelled out.
; Prototypes aren't indexed: with them, going to a function defined in the repository would always list the header
; too instead of jumping.

(function_definition declarator: (function_declarator declarator: (identifier) @name) @definition.function)
(function_definition declarator: (_ declarator: (function_declarator declarator: (identifier) @name) @definition.function))
(function_definition declarator: (_ declarator: (_ declarator: (function_declarator declarator: (identifier) @name) @definition.function)))
(preproc_function_def name: (identifier) @name) @definition.function
(preproc_def name: (identifier) @name) @definition.constant

(struct_specifier name: (type_identifier) @name body: (_)) @definition.struct
(union_specifier name: (type_identifier) @name body: (_)) @definition.struct
(enum_specifier name: (type_identifier) @name body: (_)) @definition.enum
(enumerator name: (identifier) @name) @definition.constant
(type_definition declarator: (type_identifier) @name) @definition.type
(type_definition declarator: (_ declarator: (type_identifier) @name)) @definition.type
(type_definition declarator: (function_declarator declarator: (parenthesized_declarator (pointer_declarator declarator: (type_identifier) @name)))) @definition.type

(field_declaration declarator: (field_identifier) @name) @definition.field
(field_declaration declarator: (_ declarator: (field_identifier) @name)) @definition.field
(field_declaration declarator: (_ declarator: (_ declarator: (field_identifier) @name))) @definition.field
(field_declaration declarator: (function_declarator declarator: (parenthesized_declarator (pointer_declarator declarator: (field_identifier) @name)))) @definition.field

(translation_unit (declaration declarator: [
  (identifier) @name
  (init_declarator declarator: (identifier) @name)
  (pointer_declarator declarator: (identifier) @name)
  (array_declarator declarator: (identifier) @name)
  (init_declarator declarator: (pointer_declarator declarator: (identifier) @name))
  (init_declarator declarator: (array_declarator declarator: (identifier) @name))
]) @definition.variable)
