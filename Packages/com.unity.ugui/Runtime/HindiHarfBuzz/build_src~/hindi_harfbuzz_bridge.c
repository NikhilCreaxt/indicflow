#include <hb.h>
#include <hb-ot.h>
#include <stdint.h>
#include <stdlib.h>

#if defined(_WIN32)
    #define HBHS_EXPORT __declspec(dllexport)
#else
    #define HBHS_EXPORT __attribute__((visibility("default")))
#endif

typedef struct HbHsFont
{
    hb_blob_t* blob;
    hb_face_t* face;
    hb_font_t* font;
    uint32_t upem;
} HbHsFont;

typedef struct HbHsGlyph
{
    uint32_t glyph_id;
    uint32_t cluster;
    int32_t x_advance;
    int32_t y_advance;
    int32_t x_offset;
    int32_t y_offset;
} HbHsGlyph;

HBHS_EXPORT HbHsFont* hbhs_create_font_from_file(const char* font_path, int face_index)
{
    if (font_path == NULL)
    {
        return NULL;
    }

    hb_blob_t* blob = hb_blob_create_from_file(font_path);
    if (blob == NULL || hb_blob_get_length(blob) == 0)
    {
        if (blob != NULL)
        {
            hb_blob_destroy(blob);
        }

        return NULL;
    }

    uint32_t requested_face_index = face_index < 0 ? 0u : (uint32_t)face_index;
    hb_face_t* face = hb_face_create(blob, requested_face_index);
    if (face == NULL)
    {
        hb_blob_destroy(blob);
        return NULL;
    }

    hb_font_t* font = hb_font_create(face);
    if (font == NULL)
    {
        hb_face_destroy(face);
        hb_blob_destroy(blob);
        return NULL;
    }

    hb_ot_font_set_funcs(font);

    uint32_t upem = hb_face_get_upem(face);
    if (upem == 0)
    {
        upem = 1000;
    }

    hb_font_set_scale(font, (int32_t)upem, (int32_t)upem);
    hb_font_set_ppem(font, upem, upem);

    HbHsFont* handle = (HbHsFont*)malloc(sizeof(HbHsFont));
    if (handle == NULL)
    {
        hb_font_destroy(font);
        hb_face_destroy(face);
        hb_blob_destroy(blob);
        return NULL;
    }

    handle->blob = blob;
    handle->face = face;
    handle->font = font;
    handle->upem = upem;

    return handle;
}

HBHS_EXPORT void hbhs_destroy_font(HbHsFont* handle)
{
    if (handle == NULL)
    {
        return;
    }

    if (handle->font != NULL)
    {
        hb_font_destroy(handle->font);
    }

    if (handle->face != NULL)
    {
        hb_face_destroy(handle->face);
    }

    if (handle->blob != NULL)
    {
        hb_blob_destroy(handle->blob);
    }

    free(handle);
}

HBHS_EXPORT int hbhs_get_upem(const HbHsFont* handle)
{
    if (handle == NULL)
    {
        return 0;
    }

    return (int)handle->upem;
}

HBHS_EXPORT int hbhs_shape(
    const HbHsFont* handle,
    const char* utf8,
    const char* language,
    uint32_t script_tag,
    int direction,
    HbHsGlyph* out_glyphs,
    int max_glyphs)
{
    if (handle == NULL || handle->font == NULL || utf8 == NULL || out_glyphs == NULL || max_glyphs <= 0)
    {
        return 0;
    }

    hb_buffer_t* buffer = hb_buffer_create();
    if (buffer == NULL)
    {
        return 0;
    }

    hb_buffer_add_utf8(buffer, utf8, -1, 0, -1);

    hb_direction_t text_direction = direction == 1 ? HB_DIRECTION_RTL : HB_DIRECTION_LTR;
    hb_buffer_set_direction(buffer, text_direction);

    if (script_tag != 0)
    {
        hb_script_t script = hb_script_from_iso15924_tag(script_tag);
        hb_buffer_set_script(buffer, script);
    }

    if (language != NULL && language[0] != '\0')
    {
        hb_buffer_set_language(buffer, hb_language_from_string(language, -1));
    }

    if (script_tag == 0 || language == NULL || language[0] == '\0')
    {
        hb_buffer_guess_segment_properties(buffer);
    }

    hb_shape(handle->font, buffer, NULL, 0);

    unsigned int info_count = 0;
    unsigned int position_count = 0;
    hb_glyph_info_t* glyph_infos = hb_buffer_get_glyph_infos(buffer, &info_count);
    hb_glyph_position_t* glyph_positions = hb_buffer_get_glyph_positions(buffer, &position_count);

    unsigned int glyph_count = info_count < position_count ? info_count : position_count;
    unsigned int safe_max = max_glyphs > 0 ? (unsigned int)max_glyphs : 0u;
    unsigned int out_count = glyph_count < safe_max ? glyph_count : safe_max;

    for (unsigned int i = 0; i < out_count; i++)
    {
        out_glyphs[i].glyph_id = glyph_infos[i].codepoint;
        out_glyphs[i].cluster = glyph_infos[i].cluster;
        out_glyphs[i].x_advance = glyph_positions[i].x_advance;
        out_glyphs[i].y_advance = glyph_positions[i].y_advance;
        out_glyphs[i].x_offset = glyph_positions[i].x_offset;
        out_glyphs[i].y_offset = glyph_positions[i].y_offset;
    }

    hb_buffer_destroy(buffer);
    return (int)out_count;
}
