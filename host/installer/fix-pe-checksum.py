#!/usr/bin/env python3
"""Write the PE optional-header CheckSum.

NSIS leaves that field at zero. Windows and osslsigncode then report an
invalid checksum. This is the same 32-bit sum ImageHlp CheckSumMappedFile
uses. It does not sign the file and does not change any security policy.

The dword layout matches pefile 2023.2.7 generate_checksum. When that
package is installed, this script refuses to write a different value.
"""

from __future__ import annotations

import struct
import sys
from pathlib import Path


def checksum_offset(blob: bytes) -> int:
    """Return the file offset of OptionalHeader.CheckSum. Same for PE32 and PE32+."""
    if blob[:2] != b"MZ":
        raise SystemExit("not a PE file")
    e_lfanew = struct.unpack_from("<I", blob, 0x3C)[0]
    if blob[e_lfanew : e_lfanew + 4] != b"PE\0\0":
        raise SystemExit("missing PE signature")
    # Signature is 4 bytes, COFF header is 20, CheckSum is at optional+0x40.
    return e_lfanew + 24 + 0x40


def pe_checksum(blob: bytes, offset: int) -> int:
    """Sum little-endian dwords, skip the checksum field, then add the file length."""
    checksum = 0
    remainder = len(blob) % 4
    padded = len(blob) + ((4 - remainder) * (remainder != 0))
    dwords = padded // 4
    skip = offset // 4
    for index in range(dwords):
        if index == skip:
            continue
        start = index * 4
        if index + 1 == dwords and remainder:
            chunk = blob[start:] + (b"\0" * (4 - remainder))
        else:
            chunk = blob[start : start + 4]
        checksum += struct.unpack("<I", chunk)[0]
        if checksum >= 2**32:
            checksum = (checksum & 0xFFFFFFFF) + (checksum >> 32)
    checksum = (checksum & 0xFFFF) + (checksum >> 16)
    checksum = checksum + (checksum >> 16)
    checksum = checksum & 0xFFFF
    return checksum + len(blob)


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print("usage: fix-pe-checksum.py FILE", file=sys.stderr)
        return 2
    path = Path(argv[1])
    blob = bytearray(path.read_bytes())
    offset = checksum_offset(blob)
    # The sum ignores the current checksum bytes.
    value = pe_checksum(blob, offset)
    try:
        import pefile
    except ImportError:
        pefile = None
    if pefile is not None:
        parsed = pefile.PE(str(path), fast_load=True)
        expected = parsed.generate_checksum()
        if expected != value:
            print(f"checksum {value:#x} does not match pefile {expected:#x}", file=sys.stderr)
            return 1
    struct.pack_into("<I", blob, offset, value & 0xFFFFFFFF)
    path.write_bytes(blob)
    print(f"{path} checksum {value:#010x} at offset {offset}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
