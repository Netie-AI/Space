//! Snap a click on a rendered PDF page PNG to a table cell or underline.
//! Usage: netie-pdf-detect snap-cell < stdin JSON

use image::GenericImageView;
use serde::{Deserialize, Serialize};
use std::io::{self, Read};
use std::process;

#[derive(Deserialize)]
struct In {
    #[serde(default)]
    job: String,
    #[serde(rename = "pngPath")]
    png_path: String,
    #[serde(rename = "clickXPx")]
    click_x_px: f64,
    #[serde(rename = "clickYPx")]
    click_y_px: f64,
    #[serde(rename = "pageWidthPt")]
    page_width_pt: f64,
    #[serde(rename = "pageHeightPt")]
    page_height_pt: f64,
}

#[derive(Serialize)]
struct Out {
    ok: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    kind: Option<String>,
    #[serde(rename = "xPt", skip_serializing_if = "Option::is_none")]
    x_pt: Option<f64>,
    #[serde(rename = "yFromBottom", skip_serializing_if = "Option::is_none")]
    y_from_bottom: Option<f64>,
    #[serde(rename = "wPt", skip_serializing_if = "Option::is_none")]
    w_pt: Option<f64>,
    #[serde(rename = "hPt", skip_serializing_if = "Option::is_none")]
    h_pt: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    error: Option<String>,
}

fn emit(out: &Out) {
    println!("{}", serde_json::to_string(out).unwrap_or_else(|_| r#"{"ok":false,"error":"json"}"#.into()));
}

fn main() {
    let cmd = std::env::args().nth(1).unwrap_or_default();
    if cmd != "snap-cell" {
        eprintln!("usage: netie-pdf-detect snap-cell");
        process::exit(2);
    }

    let mut raw = String::new();
    io::stdin().read_to_string(&mut raw).ok();
    let input: In = match serde_json::from_str(raw.trim()) {
        Ok(v) => v,
        Err(e) => {
            emit(&Out {
                ok: false,
                kind: None,
                x_pt: None,
                y_from_bottom: None,
                w_pt: None,
                h_pt: None,
                error: Some(format!("bad json: {e}")),
            });
            process::exit(1);
        }
    };
    let _ = input.job;

    match snap_cell(&input) {
        Ok(out) => {
            emit(&out);
            process::exit(0);
        }
        Err(e) => {
            emit(&Out {
                ok: false,
                kind: None,
                x_pt: None,
                y_from_bottom: None,
                w_pt: None,
                h_pt: None,
                error: Some(e),
            });
            process::exit(1);
        }
    }
}

fn snap_cell(input: &In) -> Result<Out, String> {
    let img = image::open(&input.png_path).map_err(|e| e.to_string())?;
    let (pw, ph) = img.dimensions();
    if pw < 8 || ph < 8 {
        return Ok(none_ok());
    }
    let page_w = input.page_width_pt.max(1.0);
    let page_h = input.page_height_pt.max(1.0);
    let px_per_pt = (pw as f64 / page_w).max(0.2);
    let win = (170.0 * px_per_pt).round() as i32;
    let cx = input.click_x_px.round() as i32;
    let cy = input.click_y_px.round() as i32;
    let x0 = (cx - win).max(0);
    let y0 = (cy - win).max(0);
    let x1 = (cx + win).min(pw as i32 - 1);
    let y1 = (cy + win).min(ph as i32 - 1);
    if x1 - x0 < 8 || y1 - y0 < 8 {
        return Ok(none_ok());
    }

    let luma = img.to_luma8();
    let dark_at = |x: i32, y: i32| -> bool {
        if x < x0 || x > x1 || y < y0 || y > y1 {
            return false;
        }
        luma.get_pixel(x as u32, y as u32)[0] < 130
    };

    let span = ((11.0 * px_per_pt).round() as i32).max(6);
    let v_line = |x: i32| -> bool {
        let a = (cy - span).max(y0);
        let b = (cy + span).min(y1);
        if b - a < span {
            return false;
        }
        let mut hit = 0;
        let mut total = 0;
        for y in a..=b {
            total += 1;
            if dark_at(x, y) || dark_at(x + 1, y) || dark_at(x - 1, y) {
                hit += 1;
            }
        }
        (hit as f64) / (total as f64) > 0.88
    };
    let h_line = |y: i32| -> bool {
        let a = (cx - span).max(x0);
        let b = (cx + span).min(x1);
        if b - a < span {
            return false;
        }
        let mut hit = 0;
        let mut total = 0;
        for x in a..=b {
            total += 1;
            if dark_at(x, y) || dark_at(x, y + 1) || dark_at(x, y - 1) {
                hit += 1;
            }
        }
        (hit as f64) / (total as f64) > 0.88
    };

    let mut left = -1;
    let mut right = -1;
    let mut top = -1;
    let mut bottom = -1;
    let mut x = cx - 2;
    while x >= x0 {
        if v_line(x) {
            left = x;
            break;
        }
        x -= 1;
    }
    x = cx + 2;
    while x <= x1 {
        if v_line(x) {
            right = x;
            break;
        }
        x += 1;
    }
    let mut y = cy - 2;
    while y >= y0 {
        if h_line(y) {
            top = y;
            break;
        }
        y -= 1;
    }
    y = cy + 2;
    while y <= y1 {
        if h_line(y) {
            bottom = y;
            break;
        }
        y += 1;
    }

    let to_pdf = |px_x: f64, px_y: f64, px_w: f64, px_h: f64, kind: &str| -> Out {
        let x_pt = px_x / pw as f64 * page_w;
        let w_pt = px_w / pw as f64 * page_w;
        let h_pt = px_h / ph as f64 * page_h;
        let y_from_bottom = page_h - ((px_y + px_h) / ph as f64) * page_h;
        Out {
            ok: true,
            kind: Some(kind.to_string()),
            x_pt: Some(x_pt),
            y_from_bottom: Some(y_from_bottom),
            w_pt: Some(w_pt),
            h_pt: Some(h_pt),
            error: None,
        }
    };

    if bottom >= 0 && (left < 0 || right < 0) {
        let h_px = (20.0 * px_per_pt).round();
        if top < 0 || (bottom as f64 - top as f64) > h_px * 2.2 {
            let x = (cx as f64 - 70.0 * px_per_pt).max(x0 as f64);
            let y = bottom as f64 - h_px - 2.0;
            return Ok(to_pdf(x, y, 150.0 * px_per_pt, h_px, "line"));
        }
    }
    if left < 0 || right < 0 || top < 0 || bottom < 0 {
        return Ok(none_ok());
    }
    if right - left < 8 || bottom - top < 6 {
        return Ok(none_ok());
    }
    Ok(to_pdf(
        (left + 2) as f64,
        (top + 2) as f64,
        (right - left - 3) as f64,
        (bottom - top - 3) as f64,
        "cell",
    ))
}

fn none_ok() -> Out {
    Out {
        ok: true,
        kind: Some("none".into()),
        x_pt: None,
        y_from_bottom: None,
        w_pt: None,
        h_pt: None,
        error: None,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use image::{GrayImage, Luma};

    #[test]
    fn underline_becomes_line_kind() {
        let mut img = GrayImage::from_pixel(200, 80, Luma([245]));
        for x in 20..180 {
            img.put_pixel(x, 60, Luma([20]));
            img.put_pixel(x, 61, Luma([20]));
        }
        let path = std::env::temp_dir().join("netie_snap_line.png");
        img.save(&path).unwrap();
        let out = snap_cell(&In {
            job: "snap-cell".into(),
            png_path: path.to_string_lossy().into(),
            click_x_px: 100.0,
            click_y_px: 50.0,
            page_width_pt: 200.0,
            page_height_pt: 80.0,
        })
        .unwrap();
        assert_eq!(out.kind.as_deref(), Some("line"));
        assert!(out.ok);
    }
}
