; Definitions for the index; see csharp.definitions.scm for the captures, and c.definitions.scm for declarators and
; why prototypes aren't indexed. Inside a class, a method's name is a field_identifier and a constructor's an
; identifier; those patterns come first so they win. `Type::method` definitions outside the class name the type as
; @receiver: the last scope before the name, so a namespace-qualified free function counts as a method of the namespace.

(field_declaration_list (function_definition declarator: (function_declarator declarator: (identifier) @name) @definition.constructor))
(field_declaration_list (declaration declarator: (function_declarator declarator: (identifier) @name) @definition.constructor))
(field_declaration_list (declaration declarator: (function_declarator declarator: (destructor_name) @name) @definition.method))
(field_declaration declarator: (function_declarator declarator: [(field_identifier) (operator_name)] @name) @definition.method)
(field_declaration declarator: (_ declarator: (function_declarator declarator: [(field_identifier) (operator_name)] @name) @definition.method))
(field_declaration declarator: (reference_declarator (function_declarator declarator: [(field_identifier) (operator_name)] @name) @definition.method))

(function_declarator declarator: (qualified_identifier scope: (_) @receiver name: [(identifier) (operator_name) (destructor_name)] @name)) @definition.method
(function_declarator declarator: (qualified_identifier name: (qualified_identifier scope: (_) @receiver name: [(identifier) (operator_name) (destructor_name)] @name))) @definition.method
(function_declarator declarator: (qualified_identifier name: (qualified_identifier name: (qualified_identifier scope: (_) @receiver name: [(identifier) (operator_name) (destructor_name)] @name)))) @definition.method

(function_definition declarator: (function_declarator declarator: [(identifier) (field_identifier) (operator_name) (destructor_name)] @name) @definition.function)
(function_definition declarator: (_ declarator: (function_declarator declarator: [(identifier) (field_identifier) (operator_name)] @name) @definition.function))
(function_definition declarator: (_ declarator: (_ declarator: (function_declarator declarator: [(identifier) (field_identifier) (operator_name)] @name) @definition.function)))
(function_definition declarator: (reference_declarator (function_declarator declarator: [(identifier) (field_identifier) (operator_name)] @name) @definition.function))
(preproc_function_def name: (identifier) @name) @definition.function
(preproc_def name: (identifier) @name) @definition.constant

(namespace_definition name: (namespace_identifier) @name) @definition.namespace
(namespace_definition name: (nested_namespace_specifier (namespace_identifier) @name)) @definition.namespace
(class_specifier name: (type_identifier) @name body: (_)) @definition.class
(struct_specifier name: (type_identifier) @name body: (_)) @definition.struct
(union_specifier name: (type_identifier) @name body: (_)) @definition.struct
(enum_specifier name: (type_identifier) @name body: (_)) @definition.enum
(enumerator name: (identifier) @name) @definition.constant
(alias_declaration name: (type_identifier) @name) @definition.type
(type_definition declarator: (type_identifier) @name) @definition.type
(type_definition declarator: (_ declarator: (type_identifier) @name)) @definition.type
(type_definition declarator: (function_declarator declarator: (parenthesized_declarator (pointer_declarator declarator: (type_identifier) @name)))) @definition.type

(field_declaration declarator: (field_identifier) @name) @definition.field
(field_declaration declarator: (_ declarator: (field_identifier) @name)) @definition.field
(field_declaration declarator: (_ declarator: (_ declarator: (field_identifier) @name))) @definition.field
(field_declaration declarator: (reference_declarator (field_identifier) @name)) @definition.field
(field_declaration declarator: (function_declarator declarator: (parenthesized_declarator (pointer_declarator declarator: (field_identifier) @name)))) @definition.field

; Variables at file or namespace level (declaration_list is also the body of `extern "C" { }`).
(translation_unit (declaration declarator: [
  (identifier) @name
  (init_declarator declarator: (identifier) @name)
  (pointer_declarator declarator: (identifier) @name)
  (array_declarator declarator: (identifier) @name)
  (init_declarator declarator: (pointer_declarator declarator: (identifier) @name))
  (init_declarator declarator: (array_declarator declarator: (identifier) @name))
]) @definition.variable)
(declaration_list (declaration declarator: [
  (identifier) @name
  (init_declarator declarator: (identifier) @name)
  (pointer_declarator declarator: (identifier) @name)
  (array_declarator declarator: (identifier) @name)
  (init_declarator declarator: (pointer_declarator declarator: (identifier) @name))
  (init_declarator declarator: (array_declarator declarator: (identifier) @name))
]) @definition.variable)

(class_specifier name: (type_identifier) @impl.type (base_class_clause [(type_identifier) (qualified_identifier) (template_type)] @impl.super))
(struct_specifier name: (type_identifier) @impl.type (base_class_clause [(type_identifier) (qualified_identifier) (template_type)] @impl.super))
