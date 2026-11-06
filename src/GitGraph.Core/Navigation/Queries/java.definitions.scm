; Definitions for the index: @name is the identifier, @definition.<kind> the whole declaration.
; Supertypes: @impl.type names the type, @impl.super each superclass or interface.

(class_declaration name: (identifier) @name) @definition.class
(record_declaration name: (identifier) @name) @definition.class
(interface_declaration name: (identifier) @name) @definition.interface
(annotation_type_declaration name: (identifier) @name) @definition.interface
(enum_declaration name: (identifier) @name) @definition.enum
(method_declaration name: (identifier) @name) @definition.method
(annotation_type_element_declaration name: (identifier) @name) @definition.method
(constructor_declaration name: (identifier) @name) @definition.constructor
(compact_constructor_declaration name: (identifier) @name) @definition.constructor
(field_declaration declarator: (variable_declarator name: (identifier) @name)) @definition.field
(constant_declaration declarator: (variable_declarator name: (identifier) @name)) @definition.constant
(record_declaration parameters: (formal_parameters (formal_parameter name: (identifier) @name) @definition.field))
(enum_constant name: (identifier) @name) @definition.constant

(class_declaration name: (identifier) @impl.type (superclass (_) @impl.super))
(class_declaration name: (identifier) @impl.type (super_interfaces (type_list (_) @impl.super)))
(record_declaration name: (identifier) @impl.type (super_interfaces (type_list (_) @impl.super)))
(enum_declaration name: (identifier) @impl.type (super_interfaces (type_list (_) @impl.super)))
(interface_declaration name: (identifier) @impl.type (extends_interfaces (type_list (_) @impl.super)))
