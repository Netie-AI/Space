//! Compact local sparse embedding — stdout JSON for Netie Space.
//! Usage: netie-embed encode --dims 256 < stdin

use std::collections::HashMap;
use std::io::{self, Read};

fn main() {
    let mut args = std::env::args().skip(1);
    let cmd = args.next().unwrap_or_default();
    if cmd != "encode" {
        eprintln!("usage: netie-embed encode --dims N");
        std::process::exit(2);
    }
    let mut dims: usize = 256;
    while let Some(a) = args.next() {
        if a == "--dims" {
            if let Some(v) = args.next() {
                dims = v.parse().unwrap_or(256).clamp(32, 1024);
            }
        }
    }

    let mut text = String::new();
    io::stdin().read_to_string(&mut text).ok();
    let emb = encode(&text, dims);
    println!("{}", emb);
}

fn encode(text: &str, dims: usize) -> String {
    let mut counts: HashMap<String, u32> = HashMap::new();
    for raw in text.to_lowercase().split(|c: char| !c.is_alphanumeric()) {
        let t = raw.trim();
        if t.len() < 3 || t.len() > 32 {
            continue;
        }
        *counts.entry(t.to_string()).or_default() += 1;
    }

    let mut values = vec![0f32; dims];
    for (token, count) in &counts {
        let h = stable_hash(token);
        let idx = (h as usize) % dims;
        let sign = if h & 1 == 0 { 1f32 } else { -1f32 };
        values[idx] += sign * (1f32 + *count as f32).ln();
    }

    let mut sum_sq = 0f64;
    for v in &values {
        sum_sq += (*v as f64) * (*v as f64);
    }
    let norm = sum_sq.sqrt();
    if norm > 1e-9 {
        for v in &mut values {
            *v = (*v as f64 / norm) as f32;
        }
    }

    let mut top: Vec<_> = counts.into_iter().collect();
    top.sort_by(|a, b| b.1.cmp(&a.1));
    let top_tokens: Vec<String> = top.into_iter().take(16).map(|(k, _)| k).collect();

    // Minimal JSON (no serde dependency)
    let vals = values
        .iter()
        .map(|v| format!("{:.6}", v))
        .collect::<Vec<_>>()
        .join(",");
    let tops = top_tokens
        .iter()
        .map(|t| format!("\"{}\"", t.replace('\\', "\\\\").replace('"', "\\\"")))
        .collect::<Vec<_>>()
        .join(",");
    format!(
        "{{\"Dimensions\":{},\"Values\":[{}],\"TopTokens\":[{}]}}",
        dims, vals, tops
    )
}

fn stable_hash(s: &str) -> u32 {
    let mut h: u32 = 2166136261;
    for b in s.bytes() {
        h ^= b as u32;
        h = h.wrapping_mul(16777619);
    }
    h
}
