#!/bin/bash

readonly compose_path="./deploy/docker/compose.yaml"

docker-compose -f $compose_path --project-name local up -d --wait