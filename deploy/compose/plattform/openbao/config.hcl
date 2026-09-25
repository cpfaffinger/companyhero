# OpenBao (A-029): Transit für Tenant-Datenschlüssel, KV für Anwendungsgeheimnisse, Raft-Speicher für Snapshots (A-031).
# Nur im internen Docker-Netz erreichbar; die Entsiegelung erfolgt in Produktion durch zwei Personen (Betrieb 3.1).
ui = false
disable_mlock = true

listener "tcp" {
  address     = "0.0.0.0:8200"
  tls_disable = true
}

storage "raft" {
  path    = "/openbao/file"
  node_id = "openbao-1"
}

api_addr     = "http://openbao:8200"
cluster_addr = "http://openbao:8201"
