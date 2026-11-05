; Definitions for the index; see csharp.definitions.scm for the captures. A function inside a class becomes a
; method (SourceParser.Definitions).

(class_definition name: (identifier) @name) @definition.class
(function_definition name: (identifier) @name) @definition.function
(module (assignment left: (identifier) @name) @definition.variable)
(class_definition body: (block (assignment left: (identifier) @name) @definition.field))

(class_definition name: (identifier) @impl.type superclasses: (argument_list (identifier) @impl.super))
(class_definition name: (identifier) @impl.type superclasses: (argument_list (attribute) @impl.super))
