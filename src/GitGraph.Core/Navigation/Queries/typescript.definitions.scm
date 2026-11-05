; Definitions for the index, shared by .ts and .tsx; see csharp.definitions.scm for the captures.

(class_declaration name: (type_identifier) @name) @definition.class
(abstract_class_declaration name: (type_identifier) @name) @definition.class
(interface_declaration name: (type_identifier) @name) @definition.interface
(type_alias_declaration name: (type_identifier) @name) @definition.type
(enum_declaration name: (identifier) @name) @definition.enum
(internal_module name: (identifier) @name) @definition.namespace
(function_declaration name: (identifier) @name) @definition.function
(generator_function_declaration name: (identifier) @name) @definition.function
(function_signature name: (identifier) @name) @definition.function
(method_definition name: (property_identifier) @name) @definition.method
(method_signature name: (property_identifier) @name) @definition.method
(abstract_method_signature name: (property_identifier) @name) @definition.method
(public_field_definition name: (property_identifier) @name) @definition.field
(property_signature name: (property_identifier) @name) @definition.property

(program (lexical_declaration (variable_declarator name: (identifier) @name value: [(arrow_function) (function_expression)]) @definition.function))
(program (export_statement declaration: (lexical_declaration (variable_declarator name: (identifier) @name value: [(arrow_function) (function_expression)]) @definition.function)))
(program (lexical_declaration (variable_declarator name: (identifier) @name) @definition.variable))
(program (export_statement declaration: (lexical_declaration (variable_declarator name: (identifier) @name) @definition.variable)))

(class_declaration name: (type_identifier) @impl.type (class_heritage (extends_clause value: (_) @impl.super)))
(class_declaration name: (type_identifier) @impl.type (class_heritage (implements_clause (_) @impl.super)))
(abstract_class_declaration name: (type_identifier) @impl.type (class_heritage (extends_clause value: (_) @impl.super)))
(abstract_class_declaration name: (type_identifier) @impl.type (class_heritage (implements_clause (_) @impl.super)))
(interface_declaration name: (type_identifier) @impl.type (extends_type_clause type: (_) @impl.super))
