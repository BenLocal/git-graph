; Definitions for the index: @name is the identifier, @definition.<kind> the whole declaration.
; Supertypes: @impl.type names the type, @impl.super each base type or interface.

(class_declaration name: (identifier) @name) @definition.class
(record_declaration name: (identifier) @name) @definition.class
(struct_declaration name: (identifier) @name) @definition.struct
(interface_declaration name: (identifier) @name) @definition.interface
(enum_declaration name: (identifier) @name) @definition.enum
(delegate_declaration name: (identifier) @name) @definition.type
(method_declaration name: (identifier) @name) @definition.method
(constructor_declaration name: (identifier) @name) @definition.constructor
(property_declaration name: (identifier) @name) @definition.property
(event_declaration name: (identifier) @name) @definition.property
(field_declaration (variable_declaration (variable_declarator name: (identifier) @name))) @definition.field
(event_field_declaration (variable_declaration (variable_declarator name: (identifier) @name))) @definition.field
(enum_member_declaration name: (identifier) @name) @definition.constant

(class_declaration name: (identifier) @impl.type (base_list (_) @impl.super))
(record_declaration name: (identifier) @impl.type (base_list (_) @impl.super))
(struct_declaration name: (identifier) @impl.type (base_list (_) @impl.super))
(interface_declaration name: (identifier) @impl.type (base_list (_) @impl.super))
