# Image de production de WinAI (FastAPI).
# Construite par le CI (.github/workflows/build-images.yml), jamais sur le serveur.
#
#   docker build -t winplus-fastapi ./python
#
# Python 3.12, comme le venv actuel du serveur. Le conteneur tourne en
# utilisateur non root (winai), système de fichiers en lecture seule. Tout ce
# que le service écrit va dans /data (volume persistant), via les variables
# fixées dans deploy/docker/compose.yml : MODEL_CACHE_DIR, HF_HOME,
# RAG_BM25_STORAGE_DIR, RAG_GRAPH_STORAGE_DIR, WINAI_USAGE_DIR,
# JWT_LEGACY_ALG_DEADLINE_FILE.

FROM python:3.12-slim AS build
RUN apt-get update \
 && apt-get install -y --no-install-recommends build-essential cmake pkg-config \
 && rm -rf /var/lib/apt/lists/*
RUN python -m venv /opt/venv
ENV PATH=/opt/venv/bin:$PATH \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1
# Outils d'installation à jour (setuptools < 78.1.1 et wheel < 0.46.2 : failles HIGH).
RUN pip install --upgrade pip "setuptools>=78.1.1" "wheel>=0.46.2"
# PyTorch CPU d'abord : la ligne « torch --index-url … » de requirements.txt
# n'est pas respectée par pip, qui installe sinon la version CUDA (plus de 4 Go
# de bibliothèques NVIDIA inutiles, l'EC2 n'a pas de GPU). Même version que
# la production (2.14.0).
ARG TORCH_VERSION=2.14.0
RUN pip install --index-url https://download.pytorch.org/whl/cpu "torch==${TORCH_VERSION}"
COPY requirements.txt /tmp/requirements.txt
RUN pip install -r /tmp/requirements.txt \
 && ! pip list 2>/dev/null | grep -qi '^nvidia-'

FROM python:3.12-slim AS final
# libgomp1 : torch et scikit-learn ; libglib2.0-0 : opencv-python-headless.
# apt-get upgrade : correctifs Debian publiés après l'image de base (ex. zlib1g).
RUN apt-get update \
 && apt-get upgrade -y --no-install-recommends \
 && apt-get install -y --no-install-recommends libgomp1 libglib2.0-0 \
 && rm -rf /var/lib/apt/lists/* \
 && python -m pip install --no-cache-dir --upgrade "setuptools>=78.1.1" "wheel>=0.46.2" \
 && useradd --uid 10001 --create-home --shell /usr/sbin/nologin winai \
 && mkdir -p /data && chown winai:winai /data

COPY --from=build /opt/venv /opt/venv
WORKDIR /app
# Le code appartient à root : lisible, pas modifiable par le service.
COPY . .

ENV PATH=/opt/venv/bin:$PATH \
    PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    UVICORN_WORKERS=2

ARG GIT_SHA=inconnu
LABEL org.opencontainers.image.title="winplus-fastapi" \
      org.opencontainers.image.revision="${GIT_SHA}"

USER winai
EXPOSE 8000
CMD ["sh", "-c", "exec uvicorn app:app --host 0.0.0.0 --port 8000 --workers ${UVICORN_WORKERS}"]
