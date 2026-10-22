# Stage 1: Build the application
FROM node:20-alpine AS build

WORKDIR /app

COPY package*.json ./
RUN npm install

COPY . .

# Версия компонента (Эпик 3K). CI подставляет её из build/components.json; локальная сборка
# остаётся на 0.0.0 — признак того, что образ собран здесь, а не пришёл из канала.
# Vite вшивает VITE_* в бандл на этапе сборки, поэтому это build-arg, а не runtime-env.
ARG VERSION=0.0.0
ENV VITE_APP_VERSION=$VERSION

RUN npm run build

# Stage 2: Serve the application using Nginx
FROM nginx:alpine

COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
