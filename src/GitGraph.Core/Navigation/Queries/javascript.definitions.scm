; Definitions for the index; see csharp.definitions.scm for the captures. Only top-level variables are indexed;
; a variable holding a function is a function.

(class_declaration name: (identifier) @name) @definition.class
(function_declaration name: (identifier) @name) @definition.function
(generator_function_declaration name: (identifier) @name) @definition.function
(method_definition name: (property_identifier) @name) @definition.method
(field_definition property: (property_identifier) @name) @definition.field

(program (lexical_declaration (variable_declarator name: (identifier) @name value: [(arrow_function) (function_expression)]) @definition.function))
(program (variable_declaration (variable_declarator name: (identifier) @name value: [(arrow_function) (function_expression)]) @definition.function))
(program (export_statement declaration: (lexical_declaration (variable_declarator name: (identifier) @name value: [(arrow_function) (function_expression)]) @definition.function)))
(program (lexical_declaration (variable_declarator name: (identifier) @name) @definition.variable))
(program (variable_declaration (variable_declarator name: (identifier) @name) @definition.variable))
(program (export_statement declaration: (lexical_declaration (variable_declarator name: (identifier) @name) @definition.variable)))

(class_declaration name: (identifier) @impl.type (class_heritage (_) @impl.super))
