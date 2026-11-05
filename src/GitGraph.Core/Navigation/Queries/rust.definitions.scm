; Definitions for the index; see csharp.definitions.scm for the captures. A function in an impl block is a method
; of the impl's type (@receiver); that pattern comes before the general function pattern so it wins.

(impl_item type: (_) @receiver body: (declaration_list (function_item name: (identifier) @name) @definition.method))
(function_signature_item name: (identifier) @name) @definition.method
(function_item name: (identifier) @name) @definition.function
(trait_item name: (type_identifier) @name) @definition.interface
(struct_item name: (type_identifier) @name) @definition.struct
(union_item name: (type_identifier) @name) @definition.struct
(enum_item name: (type_identifier) @name) @definition.enum
(type_item name: (type_identifier) @name) @definition.type
(const_item name: (identifier) @name) @definition.constant
(static_item name: (identifier) @name) @definition.constant
(mod_item name: (identifier) @name) @definition.namespace
(macro_definition name: (identifier) @name) @definition.function
(field_declaration name: (field_identifier) @name) @definition.field
(enum_variant name: (identifier) @name) @definition.constant

(impl_item trait: (_) @impl.super type: (_) @impl.type)
(trait_item name: (type_identifier) @impl.type bounds: (trait_bounds (_) @impl.super))
