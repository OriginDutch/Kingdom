// Terrain: vertex colours (sRGB) with flat, faceted low-poly lighting.
shader_type spatial;
render_mode cull_back;

uniform float roughness = 0.95;

void fragment() {
	// Vertex colours are authored in sRGB; lighting works in linear space.
	ALBEDO = pow(COLOR.rgb, vec3(2.2));
	ROUGHNESS = roughness;
	// Per-triangle normal from screen-space derivatives gives the faceted look.
	vec3 flat_n = normalize(cross(dFdx(VERTEX), dFdy(VERTEX)));
	if (dot(flat_n, NORMAL) < 0.0) {
		flat_n = -flat_n;
	}
	NORMAL = flat_n;
}
