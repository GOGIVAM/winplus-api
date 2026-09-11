"""Appel générique à un LLM local chargé via models/loader.py."""

from __future__ import annotations

import torch

from RAG.self_hosted import config


def generate(tokenizer, model, system_prompt: str, user_prompt: str, max_new_tokens: int = 800, thinking: bool = False) -> str:
    messages = [
        {"role": "system", "content": system_prompt},
        {"role": "user", "content": user_prompt},
    ]
    kwargs = {}
    try:
        text = tokenizer.apply_chat_template(
            messages, tokenize=False, add_generation_prompt=True, enable_thinking=thinking
        )
    except TypeError:
        # Certains tokenizers Qwen n'exposent pas enable_thinking selon la version.
        text = tokenizer.apply_chat_template(messages, tokenize=False, add_generation_prompt=True)

    inputs = tokenizer(text, return_tensors="pt").to(config.DEVICE)
    with torch.no_grad():
        output_ids = model.generate(
            **inputs,
            max_new_tokens=max_new_tokens,
            do_sample=False,
            temperature=None,
            top_p=None,
        )
    generated = output_ids[:, inputs["input_ids"].shape[1] :]
    return tokenizer.decode(generated[0], skip_special_tokens=True).strip()
