"""
Translates maps and configs written in the old (Portuguese) format into the current English format.

Same rules as Server/Compat/LegacyNames.cs; the tables are in Server/legacy-names.json.
"""

import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_TABLE = os.path.normpath(os.path.join(HERE, "..", "..", "Server", "legacy-names.json"))


def load_json(path):
    """Reads JSON that may have // comments and trailing commas (like the server does)."""
    text = open(path, encoding="utf-8-sig").read()
    out, i, n, in_str = [], 0, len(text), False
    while i < n:
        c = text[i]
        if in_str:
            out.append(c)
            if c == "\\" and i + 1 < n:
                out.append(text[i + 1])
                i += 2
                continue
            if c == '"':
                in_str = False
        elif c == '"':
            in_str = True
            out.append(c)
        elif c == "/" and i + 1 < n and text[i + 1] == "/":
            while i < n and text[i] != "\n":
                i += 1
            continue
        elif c == "/" and i + 1 < n and text[i + 1] == "*":
            i = text.find("*/", i + 2) + 2
            continue
        else:
            out.append(c)
        i += 1
    return json.loads(re.sub(r",(\s*[}\]])", r"\1", "".join(out)))


class LegacyNames:
    def __init__(self, table_path=DEFAULT_TABLE):
        t = load_json(table_path)
        low = lambda d: {k.lower(): v for k, v in d.items()}
        self.keys = low(t["keys"])
        self.names = low(t["names"])
        self.values = low(t["values"])
        self.bark_keys = low(t["barkKeys"])
        self.prefixes = t["prefixes"]
        self.formats = low(t["formats"])
        self.name_dicts = {x.lower() for x in t["nameDictionaries"]}

    def value(self, s):
        if not s:
            return s
        for table in (self.values, self.names, self.formats):
            if s.lower() in table:
                return table[s.lower()]
        if "|" in s:
            return "|".join(self.value(p) for p in s.split("|"))
        if ":" in s:
            parts = s.split("+")
            for i, p in enumerate(parts):
                for old, new in self.prefixes.items():
                    if p.lower().startswith(old):
                        rest = p[len(old):]
                        parts[i] = new + self.names.get(rest.lower(), rest)
            return "+".join(parts)
        return s

    def translate(self, node, parent=None):
        """Returns a translated copy of a parsed JSON document."""
        if isinstance(node, dict):
            names = parent is not None and parent.lower() in self.name_dicts
            barks = parent is not None and parent.lower() == "barks"
            table = self.names if names else self.bark_keys if barks else self.keys
            out = {}
            for k, v in node.items():
                nk = table.get(k.lower(), k)
                # a new key that is already in the file wins over the translated old one
                if nk in out and nk != k:
                    continue
                out[nk] = self.translate(v, None if names or barks else nk)
            return out
        if isinstance(node, list):
            # elements of a list are never "dictionaries of author-chosen names" (a wave's "groups" is a list)
            return [self.translate(x, None) for x in node]
        if isinstance(node, str):
            return self.value(node)
        return node
