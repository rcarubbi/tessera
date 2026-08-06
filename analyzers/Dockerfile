FROM node:24-alpine
WORKDIR /app
COPY package*.json ./
RUN npm ci --omit=dev
COPY src/ src/
EXPOSE 4350
CMD ["node", "src/index.js"]
