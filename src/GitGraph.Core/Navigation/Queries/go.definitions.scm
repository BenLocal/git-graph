; Definitions for the index; see csharp.definitions.scm for the captures. Go interfaces are structural, so there
; are no @impl captures.

(function_declaration name: (identifier) @name) @definition.function
(method_declaration
  receiver: (parameter_list (parameter_declaration type: (_) @receiver))
  name: (field_identifier) @name) @definition.method
(type_spec name: (type_identifier) @name type: (struct_type)) @definition.struct
(type_spec name: (type_identifier) @name type: (interface_type)) @definition.interface
(type_spec name: (type_identifier) @name) @definition.type
(type_alias name: (type_identifier) @name) @definition.type
(method_elem name: (field_identifier) @name) @definition.method
(field_declaration name: (field_identifier) @name) @definition.field
(source_file (const_declaration (const_spec name: (identifier) @name) @definition.constant))
(source_file (var_declaration (var_spec name: (identifier) @name) @definition.variable))
