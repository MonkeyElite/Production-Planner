FROM postgres:16

# Custom entrypoint wrapper
COPY docker-entrypoint-ssl.sh /usr/local/bin/docker-entrypoint-ssl.sh
RUN chmod +x /usr/local/bin/docker-entrypoint-ssl.sh

ENTRYPOINT ["docker-entrypoint-ssl.sh"]
CMD ["postgres"]
