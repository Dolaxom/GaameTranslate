"""Optional verification using the official `anki` Python package in an isolated environment."""
import json
import pathlib
import sys
import tempfile

from anki.collection import Collection
from anki.import_export_pb2 import ImportAnkiPackageRequest, ImportAnkiPackageOptions

package = pathlib.Path(sys.argv[1]).resolve()
with tempfile.TemporaryDirectory(prefix="gaame-anki-verify-") as directory:
    collection = Collection(str(pathlib.Path(directory) / "collection.anki2"))
    try:
        request = ImportAnkiPackageRequest(package_path=str(package), options=ImportAnkiPackageOptions())
        result = collection.import_anki_package(request)
        notes = collection.note_count()
        cards = collection.card_count()
        assert notes > 0 and cards >= notes, (notes, cards)
        collection.import_anki_package(request)
        assert collection.note_count() == notes, "Reimport created duplicate notes"
        print(json.dumps({"package": package.name, "notes": notes, "cards": cards, "reimport_duplicates": 0, "anki_engine": "26.9.3"}))
    finally:
        collection.close()
