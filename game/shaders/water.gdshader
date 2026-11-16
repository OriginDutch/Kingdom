// Simple semi-transparent water for sea, lakes and rivers. Placeholder until a proper water pass.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque;

uniform vec4 water_color : source_color = vec4(0.12, 0.34, 0.48, 1.0);
uniform float opacity = 0.82;

void fragment() {
	ALBEDO = water_color.rgb;
	ALPHA = opacity;
	ROUGHNESS = 0.08;
	METALLIC = 0.0;
	SPECULAR = 0.6;
}
